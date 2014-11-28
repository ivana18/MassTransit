// Copyright 2007-2014 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.AzureServiceBusTransport.Pipeline
{
    using System;
    using System.Threading.Tasks;
    using Logging;
    using MassTransit.Pipeline;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;


    /// <summary>
    /// Creates a message receiver and receives messages from the input queue of the endpoint
    /// </summary>
    public class MessageReceiverFilter :
        IFilter<ConnectionContext>
    {
        static readonly ILog _log = Logger.Get<MessageReceiverFilter>();
        readonly IPipe<ReceiveContext> _receivePipe;

        public MessageReceiverFilter(IPipe<ReceiveContext> receivePipe)
        {
            _receivePipe = receivePipe;
        }

        async Task IFilter<ConnectionContext>.Send(ConnectionContext context, IPipe<ConnectionContext> next)
        {
            var receiveSettings = context.GetPayload<ReceiveSettings>();

            string queuePath = receiveSettings.QueueDescription.Path;

            Uri inputAddress = context.GetQueueAddress(queuePath);

            if (_log.IsDebugEnabled)
                _log.DebugFormat("Creating message receiver for {0}", inputAddress);

            MessageReceiver messageReceiver = await context.GetMessagingFactory().CreateMessageReceiverAsync(queuePath, ReceiveMode.PeekLock);
            messageReceiver.PrefetchCount = receiveSettings.PrefetchCount;
            messageReceiver.RetryPolicy = RetryPolicy.Default;

            using (var receiver = new Receiver(messageReceiver, inputAddress, _receivePipe, receiveSettings, context.CancellationToken))
            {
                ReceiverMetrics metrics = await receiver.CompleteTask;

                if (_log.IsDebugEnabled)
                {
                    _log.DebugFormat("Consumer {0}: {1} received, {2} concurrent", queuePath,
                        metrics.DeliveryCount,
                        metrics.ConcurrentDeliveryCount);
                }
            }

            if(!messageReceiver.IsClosed)
                await messageReceiver.CloseAsync();

            await next.Send(context);
        }

        public bool Inspect(IPipeInspector inspector)
        {
            return inspector.Inspect(this, x => _receivePipe.Inspect(inspector));
        }
    }
}