﻿// Copyright 2018 Confluent Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Refer to LICENSE for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka.Serialization;


namespace Confluent.Kafka.Examples.AvroSpecific
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Usage: .. bootstrapServers schemaRegistryUrl topicName");
                return;
            }

            string bootstrapServers = args[0];
            string schemaRegistryUrl = args[1];
            string topicName = args[2];

            var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };

            var avroConfig = new AvroSerdeProviderConfig
            {
                // Note: you can specify more than one schema registry url using the
                // schema.registry.url property for redundancy (comma separated list). 
                // The property name is not plural to follow the convention set by
                // the Java implementation.
                SchemaRegistryUrl = schemaRegistryUrl,
                // optional schema registry client properties:
                SchemaRegistryRequestTimeoutMs = 5000,
                SchemaRegistryMaxCachedSchemas = 10,
                // optional avro serializer properties:
                AvroSerializerBufferBytes = 50,
                AvroSerializerAutoRegisterSchemas = true
            };

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = Guid.NewGuid().ToString()
            };

            // Note: The User class in this project was generated using the Confluent fork of the avrogen.exe tool 
            // (avaliable from: https://github.com/confluentinc/avro/tree/confluent-fork) which includes modifications
            // that prevent namespace clashes with user namespaces that include the identifier 'Avro'. AvroSerializer
            // and AvroDeserializer are also compatible with classes generated by the official avrogen.exe tool 
            // (available from: https://github.com/apache/avro), with the above limitation.

            CancellationTokenSource cts = new CancellationTokenSource();
            var consumeTask = Task.Run(() =>
            {
                using (var serdeProvider = new AvroSerdeProvider(avroConfig))
                using (var consumer = new Consumer<string, User>(consumerConfig, serdeProvider.GetDeserializerGenerator<string>(), serdeProvider.GetDeserializerGenerator<User>()))
                {
                    consumer.OnError += (_, e)
                        => Console.WriteLine($"Error: {e.Reason}");

                    consumer.Subscribe(topicName);

                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var consumeResult = consumer.Consume(cts.Token);
                            Console.WriteLine($"user key name: {consumeResult.Message.Key}, user value favorite color: {consumeResult.Value.favorite_color}");
                        }
                        catch (ConsumeException e)
                        {
                            Console.WriteLine("Consume error: " + e.Error.Reason);
                        }
                    }

                    consumer.Close();
                }
            }, cts.Token);

            using (var serdeProvider = new AvroSerdeProvider(avroConfig))
            using (var producer = new Producer<string, User>(producerConfig, serdeProvider.GetSerializerGenerator<string>(), serdeProvider.GetSerializerGenerator<User>()))
            {
                Console.WriteLine($"{producer.Name} producing on {topicName}. Enter user names, q to exit.");

                int i = 0;
                string text;
                while ((text = Console.ReadLine()) != "q")
                {
                    User user = new User { name = text, favorite_color = "green", favorite_number = i++ };
                    producer
                        .ProduceAsync(topicName, new Message<string, User> { Key = text, Value = user})
                        .ContinueWith(task => task.IsFaulted
                            ? $"error producing message: {task.Exception.Message}"
                            : $"produced to: {task.Result.TopicPartitionOffset}");
                }
            }

            cts.Cancel();
        }
    }
}
