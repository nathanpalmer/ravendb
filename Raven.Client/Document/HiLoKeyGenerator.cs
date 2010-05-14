﻿using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using Raven.Client.Client;
using Raven.Database.Exceptions;
using Raven.Database.Json;

namespace Raven.Client.Document
{
    public class HiLoKeyGenerator
    {
        private const string RavenKeyGeneratorsHilo = "Raven/KeyGenerators/Hilo";
        private readonly IDatabaseCommands commands;
        private readonly long capacity;
        private readonly object generatorLock = new object();
        private long currentHi;
        private long currentLo;

        public HiLoKeyGenerator(IDatabaseCommands commands,long capacity)
        {
            currentHi = 0;
            this.commands = commands;
            this.capacity = capacity;
            currentLo = capacity + 1;
        }

        public string GenerateDocumentKey(DocumentConvention conventions, object entity)
        {
            return string.Format("{0}/{1}",
                                 conventions.GetTypeTagName(entity.GetType()).ToLowerInvariant(),
                                 NextId());
        }

        private long NextId()
        {
            long incrementedCurrentLow = Interlocked.Increment(ref currentLo);
            if (incrementedCurrentLow >= capacity)
            {
                lock (generatorLock)
                {
                    if (Thread.VolatileRead(ref currentLo) >= capacity)
                    {
                        currentHi = GetNextHi();
                        currentLo = 0;
                        incrementedCurrentLow = 0;
                    }
                }
            }
            return (currentHi - 1)*capacity + (incrementedCurrentLow);
        }

        private long GetNextHi()
        {
            while (true)
            {
                try
                {
                    var document = commands.Get(RavenKeyGeneratorsHilo);
                    if (document == null)
                    {
                        commands.Put(RavenKeyGeneratorsHilo,
                                     Guid.Empty,
                                     // sending empty guid means - ensure the that the document does NOT exists
                                     JObject.FromObject(new HiLoKey{ServerHi = 1}),
                                     new JObject());
                        return 1;
                    }
                    var hiLoKey = document.DataAsJson.JsonDeserialization<HiLoKey>();
                    var newHi = hiLoKey.ServerHi;
                    hiLoKey.ServerHi += 1;
                    commands.Put(RavenKeyGeneratorsHilo, document.Etag,
                                 JObject.FromObject(hiLoKey),
                                 document.Metadata);
                    return newHi;
                }
                catch (ConcurrencyException)
                {
                   // expected, we need to retry
                }
            }
        }

        #region Nested type: HiLoKey

        private class HiLoKey
        {
            public long ServerHi { get; set; }

        }

        #endregion
    }
}