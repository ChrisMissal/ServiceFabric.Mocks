﻿using System;
using System.Collections.Concurrent;
using System.Fabric;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Data.Collections.Preview;
using Microsoft.ServiceFabric.Data.Notifications;

namespace ServiceFabric.Mocks
{
    /// <summary>
    /// Defines replica of a reliable state provider.
    /// </summary>
    public class MockReliableStateManager : IReliableStateManagerReplica
    {
        private ConcurrentDictionary<Uri, IReliableState> _store = new ConcurrentDictionary<Uri, IReliableState>();

        /// <summary>
        /// Returns last known <see cref="ReplicaRole"/>.
        /// </summary>
        public ReplicaRole? ReplicaRole { get; set; }

        /// <summary>
        /// Occurs when State Manager's state changes.
        /// For example, creation or delete of reliable state or rebuild of the reliable state manager.
        /// </summary>
        public event EventHandler<NotifyStateManagerChangedEventArgs> StateManagerChanged;

        /// <summary>
        /// Does not fire.
        /// </summary>
        public event EventHandler<NotifyTransactionChangedEventArgs> TransactionChanged;

        /// <summary>
        /// Called when <see cref="TriggerDataLoss"/> is called.
        /// </summary>
        public Func<CancellationToken, Task<bool>> OnDataLossAsync { set; get; }

        public void Abort()
        {
        }

        public Task BackupAsync(Func<BackupInfo, CancellationToken, Task<bool>> backupCallback)
        {
            return BackupAsync(BackupOption.Full, TimeSpan.MaxValue, CancellationToken.None, backupCallback);
        }

        public Task BackupAsync(BackupOption option, TimeSpan timeout, CancellationToken cancellationToken, Func<BackupInfo, CancellationToken, Task<bool>> backupCallback)
        {
            string stateBin = Path.Combine(Path.GetTempPath(), "state.bin");
            using (var fs = File.Create(stateBin))
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(fs, _store);
            }
            var info = new BackupInfo(Path.GetDirectoryName(stateBin), option, new BackupInfo.BackupVersion());
            return backupCallback(info, CancellationToken.None);
        }

        public Task ChangeRoleAsync(ReplicaRole newRole, CancellationToken cancellationToken)
        {
            ReplicaRole = newRole;
            return Task.FromResult(true);
        }

        public Task ClearAsync(ITransaction tx)
        {
            return ClearAsync();
        }

        public Task ClearAsync()
        {
            _store.Clear();
            return Task.FromResult(true);
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public ITransaction CreateTransaction()
        {
            return new MockTransaction();
        }

        public IAsyncEnumerator<IReliableState> GetAsyncEnumerator()
        {
            return new MockAsyncEnumerator<IReliableState>(_store.Values);
        }

        public Task<T> GetOrAddAsync<T>(string name) where T : IReliableState
        {
            return GetOrAddAsync<T>(null, name);
        }

        public Task<T> GetOrAddAsync<T>(ITransaction tx, string name) where T : IReliableState
        {
            var uri = CreateUri(name);
            bool added = !_store.ContainsKey(uri);
            
            Func<Uri, IReliableState> addValueFactory = _=>
            { 
                IReliableState reliable;
                var typeArguments = typeof(T).GetGenericArguments();
                var typeDefinition = typeof(T).GetGenericTypeDefinition();

                if (typeof(IReliableDictionary<,>).IsAssignableFrom(typeDefinition))
                {
                    Type dictionaryType = typeof(MockReliableDictionary<,>);
                    reliable = (IReliableState) Activator.CreateInstance(dictionaryType.MakeGenericType(typeArguments));
                }
                else if (typeof(IReliableConcurrentQueue<>).IsAssignableFrom(typeDefinition))
                {
                    Type queueType = typeof(MockReliableConcurrentQueue<>);
                    reliable = (IReliableState)Activator.CreateInstance(queueType.MakeGenericType(typeArguments));
                }
                else
                {
                    Type queueType = typeof(MockReliableQueue<>);
                    reliable = (IReliableState) Activator.CreateInstance(queueType.MakeGenericType(typeArguments));
                }
                return reliable;
            };
            var result = (T)_store.GetOrAdd(uri, addValueFactory);
            if (added)
            {
                OnStateManagerChanged(new NotifyStateManagerSingleEntityChangedEventArgs(tx, result, NotifyStateManagerChangedAction.Add));
            }
            return Task.FromResult(result);
        }

        public Task<T> GetOrAddAsync<T>(string name, TimeSpan timeout) where T : IReliableState
        {
            return GetOrAddAsync<T>(name);
        }

        public Task<T> GetOrAddAsync<T>(ITransaction tx, string name, TimeSpan timeout) where T : IReliableState
        {
            return GetOrAddAsync<T>(tx, name);
        }

        public Task<T> GetOrAddAsync<T>(Uri name) where T : IReliableState
        {
            return GetOrAddAsync<T>(name.ToString());
        }

        public Task<T> GetOrAddAsync<T>(Uri name, TimeSpan timeout) where T : IReliableState
        {
            return GetOrAddAsync<T>(name.ToString());
        }

        public Task<T> GetOrAddAsync<T>(ITransaction tx, Uri name) where T : IReliableState
        {
            return GetOrAddAsync<T>(tx, name.ToString());
        }

        public Task<T> GetOrAddAsync<T>(ITransaction tx, Uri name, TimeSpan timeout) where T : IReliableState
        {
            return GetOrAddAsync<T>(tx, name.ToString());
        }

        public void Initialize(StatefulServiceInitializationParameters initializationParameters)
        {

        }

        public Task<IReplicator> OpenAsync(ReplicaOpenMode openMode, IStatefulServicePartition partition, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReplicator>(null);
        }

        public Task RemoveAsync(string name)
        {
            return RemoveAsync(null, name);
        }

        public Task RemoveAsync(ITransaction tx, string name)
        {
            IReliableState result;
            if (_store.TryRemove(CreateUri(name), out result))
            {
                OnStateManagerChanged(new NotifyStateManagerSingleEntityChangedEventArgs(tx, result, NotifyStateManagerChangedAction.Remove));
            }
            return Task.FromResult(true);
        }

        public Task RemoveAsync(string name, TimeSpan timeout)
        {
            return RemoveAsync(name);
        }

        public Task RemoveAsync(ITransaction tx, string name, TimeSpan timeout)
        {
            return RemoveAsync(tx, name);
        }

        public Task RemoveAsync(Uri name)
        {
            return RemoveAsync(name.ToString());
        }

        public Task RemoveAsync(Uri name, TimeSpan timeout)
        {
            return RemoveAsync(name.ToString());
        }

        public Task RemoveAsync(ITransaction tx, Uri name)
        {
            return RemoveAsync(tx, name.ToString());
        }

        public Task RemoveAsync(ITransaction tx, Uri name, TimeSpan timeout)
        {
            return RemoveAsync(tx, name.ToString());
        }

        public Task RestoreAsync(string backupFolderPath)
        {
            string stateBin = Path.Combine(backupFolderPath, "state.bin");

            if (!File.Exists(stateBin))
            {
                throw new InvalidOperationException("No backed up state exists.");
            }

            using (var fs = File.OpenRead(stateBin))
            {
                var formatter = new BinaryFormatter();
                _store = (ConcurrentDictionary<Uri, IReliableState>)formatter.Deserialize(fs);
            }
            return Task.FromResult(true);
        }

        public Task RestoreAsync(string backupFolderPath, RestorePolicy restorePolicy, CancellationToken cancellationToken)
        {
            return RestoreAsync(backupFolderPath);
        }

        public bool TryAddStateSerializer<T>(IStateSerializer<T> stateSerializer)
        {
            return true;
        }

        public Task<ConditionalValue<T>> TryGetAsync<T>(string name) where T : IReliableState
        {
            IReliableState item;
            bool result = _store.TryGetValue(CreateUri(name), out item);

            return Task.FromResult(new ConditionalValue<T>(result, (T)item));
        }

        public Task<ConditionalValue<T>> TryGetAsync<T>(Uri name) where T : IReliableState
        {
            return TryGetAsync<T>(name.ToString());
        }

        public Task TriggerDataLoss()
        {
            return OnDataLossAsync(CancellationToken.None);
        }

        private static Uri CreateUri(string name)
        {
            return new Uri($"fabric://mocks/{name}", UriKind.Absolute);
        }

        public void OnStateManagerChanged(NotifyStateManagerChangedEventArgs e)
        {
            StateManagerChanged?.Invoke(this, e);
        }

        public void OnTransactionChanged(NotifyTransactionChangedEventArgs e)
        {
            TransactionChanged?.Invoke(this, e);
        }
    }
}