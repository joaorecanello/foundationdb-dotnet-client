﻿#region BSD Licence
/* Copyright (c) 2013, Doxense SARL
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
	* Redistributions of source code must retain the above copyright
	  notice, this list of conditions and the following disclaimer.
	* Redistributions in binary form must reproduce the above copyright
	  notice, this list of conditions and the following disclaimer in the
	  documentation and/or other materials provided with the distribution.
	* Neither the name of the <organization> nor the
	  names of its contributors may be used to endorse or promote products
	  derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
#endregion

// enable this to help debug Transactions
#undef DEBUG_TRANSACTIONS

using FoundationDb.Client.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FoundationDb.Client
{

	public class FdbTransaction : IDisposable
	{
		private readonly FdbDatabase m_database;
		private readonly int m_id;
		private readonly TransactionHandle m_handle;
		private bool m_disposed;
		/// <summary>Estimated size of written data (in bytes)</summary>
		private int m_payloadBytes;

		internal FdbTransaction(FdbDatabase database, int id, TransactionHandle handle)
		{
			m_database = database;
			m_id = id;
			m_handle = handle;
		}

		public int Id { get { return m_id; } }

		public FdbDatabase Database { get { return m_database; } }

		internal TransactionHandle Handle { get { return m_handle; } }

		internal bool StillAlive { get { return !m_disposed; } }

		#region Options..

		/// <summary>Allows this transaction to read and modify system keys (those that start with the byte 0xFF)</summary>
		public void WithAccessToSystemKeys()
		{
			SetOption(FdbTransactionOption.AccessSystemKeys, null);
		}

		/// <summary>Specifies that this transaction should be treated as highest priority and that lower priority transactions should block behind this one. Use is discouraged outside of low-level tools</summary>
		public void WithPrioritySystemImmediate()
		{
			SetOption(FdbTransactionOption.PrioritySystemImmediate, null);
		}

		/// <summary>Specifies that this transaction should be treated as low priority and that default priority transactions should be processed first. Useful for doing batch work simultaneously with latency-sensitive work</summary>
		public void WithPriorityBatch()
		{
			SetOption(FdbTransactionOption.PriorityBatch, null);
		}

		/// <summary>Set a parameter-less option on this transaction</summary>
		/// <param name="option">Option to set</param>
		public void SetOption(FdbTransactionOption option)
		{
			SetOption(option, default(string));
		}

		/// <summary>Set an option on this transaction</summary>
		/// <param name="option">Option to set</param>
		/// <param name="value">Value of the parameter</param>
		public void SetOption(FdbTransactionOption option, string value)
		{
			ThrowIfDisposed();

			Fdb.EnsureNotOnNetworkThread();

			var data = FdbNative.ToNativeString(value, nullTerminated: true);
			unsafe
			{
				fixed (byte* ptr = data.Array)
				{
					FdbNative.TransactionSetOption(m_handle, option, ptr + data.Offset, data.Count);
				}
			}
		}

		#endregion

		#region Versions...

		/// <summary>Returns this transaction snapshot read version.</summary>
		public Task<long> GetReadVersionAsync(CancellationToken ct = default(CancellationToken))
		{
			ThrowIfDisposed();

			Fdb.EnsureNotOnNetworkThread();

			var future = FdbNative.TransactionGetReadVersion(m_handle);
			return FdbFuture.CreateTaskFromHandle(future,
				(h) =>
				{
					long version;
					var err = FdbNative.FutureGetVersion(h, out version);
#if DEBUG_TRANSACTIONS
					Debug.WriteLine("FdbTransaction[" + m_id + "].GetReadVersion() => err=" + err + ", version=" + version);
#endif
					Fdb.DieOnError(err);
					return version;
				},
				ct
			);
		}

		/// <summary>Retrieves the database version number at which a given transaction was committed.</summary>
		/// <returns>Version number, or -1 if this transaction was not committed (or did nothing)</returns>
		/// <remarks>The value return by this method is undefined if Commit has not been called</remarks>
		public long GetCommittedVersion()
		{
			ThrowIfDisposed();

			Fdb.EnsureNotOnNetworkThread();

			long version;
			var err = FdbNative.TransactionGetCommittedVersion(m_handle, out version);
#if DEBUG_TRANSACTIONS
			Debug.WriteLine("FdbTransaction[" + m_id + "].GetCommittedVersion() => err=" + err + ", version=" + version);
#endif
			Fdb.DieOnError(err);
			return version;
		}

		public void SetReadVersion(long version)
		{
			ThrowIfDisposed();

			Fdb.EnsureNotOnNetworkThread();

			FdbNative.TransactionSetReadVersion(m_handle, version);
		}

		#endregion

		#region Get...

		private static bool TryGetValueResult(FutureHandle h, out ArraySegment<byte> result)
		{
			bool present;
			var err = FdbNative.FutureGetValue(h, out present, out result);
#if DEBUG_TRANSACTIONS
			Debug.WriteLine("FdbTransaction[].TryGetValueResult() => err=" + err + ", present=" + present + ", valueLength=" + result.Count);
#endif
			Fdb.DieOnError(err);
			return present;
		}

		private static ArraySegment<byte> GetValueResultBytes(FutureHandle h)
		{
			ArraySegment<byte> result;
			if (!TryGetValueResult(h, out result))
			{
				return Fdb.Nil;
			}
			return result;
		}

		internal Task<ArraySegment<byte>> GetCoreAsync(ArraySegment<byte> key, bool snapshot, CancellationToken ct)
		{
			Fdb.EnsureKeyIsValid(key);

			var future = FdbNative.TransactionGet(m_handle, key, snapshot);
			return FdbFuture.CreateTaskFromHandle(future, (h) => GetValueResultBytes(h), ct);
		}

		internal ArraySegment<byte> GetCore(ArraySegment<byte> key, bool snapshot, CancellationToken ct)
		{
			Fdb.EnsureKeyIsValid(key);

			var handle = FdbNative.TransactionGet(m_handle, key, snapshot);
			using (var future = FdbFuture.FromHandle(handle, (h) => GetValueResultBytes(h), ct, willBlockForResult: true))
			{
				return future.GetResult();
			}
		}

#if DEPRECATED

		/// <summary>Returns the value of a particular key</summary>
		/// <param name="key">Key to retrieve (UTF-8)</param>
		/// <param name="snapshot"></param>
		/// <param name="ct">CancellationToken used to cancel this operation</param>
		/// <returns>Task that will return the value of the key if it is found, null if the key does not exist, or an exception</returns>
		/// <exception cref="System.ArgumentException">If the key is null or empty</exception>
		/// <exception cref="System.OperationCanceledException">If the cancellation token is already triggered</exception>
		/// <exception cref="System.ObjectDisposedException">If the transaction has already been completed</exception>
		/// <exception cref="System.InvalidOperationException">If the operation method is called from the Network Thread</exception>
		public Task<byte[]> GetAsync(string key, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return GetAsync(Fdb.GetKeyBytes(key), snapshot, ct);
		}
#endif

		public Task<ArraySegment<byte>> GetAsync(IFdbKey key, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return GetAsync(key.ToBytes(), snapshot, ct);
		}

		public async Task<byte[]> GetBytesAsync(IFdbKey key,bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return Fdb.ToByteArray(await GetAsync(key.ToBytes(), snapshot, ct));
		}

#if DEPRECATED
		/// <summary>Returns the value of a particular key</summary>
		/// <param name="key">Key to retrieve</param>
		/// <param name="snapshot"></param>
		/// <param name="ct">CancellationToken used to cancel this operation</param>
		/// <returns>Task that will return null if the value of the key if it is found, null if the key does not exist, or an exception</returns>
		/// <exception cref="System.ArgumentException">If the key is null or empty</exception>
		/// <exception cref="System.OperationCanceledException">If the cancellation token is already triggered</exception>
		/// <exception cref="System.ObjectDisposedException">If the transaction has already been completed</exception>
		/// <exception cref="System.InvalidOperationException">If the operation method is called from the Network Thread</exception>
		public Task<byte[]> GetAsync(byte[] key, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return GetAsync(new ArraySegment<byte>(key), snapshot, ct);
		}
#endif

		/// <summary>Returns the value of a particular key</summary>
		/// <param name="keyBytes">Key to retrieve</param>
		/// <param name="snapshot"></param>
		/// <param name="ct">CancellationToken used to cancel this operation</param>
		/// <returns>Task that will return null if the value of the key if it is found, null if the key does not exist, or an exception</returns>
		/// <exception cref="System.ArgumentException">If the key is null or empty</exception>
		/// <exception cref="System.OperationCanceledException">If the cancellation token is already triggered</exception>
		/// <exception cref="System.ObjectDisposedException">If the transaction has already been completed</exception>
		/// <exception cref="System.InvalidOperationException">If the operation method is called from the Network Thread</exception>
		public Task<ArraySegment<byte>> GetAsync(ArraySegment<byte> keyBytes, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			ct.ThrowIfCancellationRequested();
			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			return GetCoreAsync(keyBytes, snapshot, ct);
		}

#if DEPRECATED
		/// <summary>Returns the value of a particular key</summary>
		/// <param name="key">Key to retrieve (UTF-8)</param>
		/// <param name="snapshot"></param>
		/// <param name="ct">CancellationToken used to cancel this operation</param>
		/// <returns>Returns the value of the key if it is found, or null if the key does not exist</returns>
		/// <exception cref="System.ArgumentException">If the key is null or empty</exception>
		/// <exception cref="System.OperationCanceledException">If the cancellation token is already triggered</exception>
		/// <exception cref="System.ObjectDisposedException">If the transaction has already been completed</exception>
		/// <exception cref="System.InvalidOperationException">If the operation method is called from the Network Thread</exception>
		public byte[] Get(string key, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return Get(Fdb.GetKeyBytes(key), snapshot, ct);
		}

		/// <summary>Returns the value of a particular key</summary>
		/// <param name="key">Key to retrieve (UTF-8)</param>
		/// <param name="snapshot"></param>
		/// <param name="ct">CancellationToken used to cancel this operation</param>
		/// <returns>Returns the value of the key if it is found, or null if the key does not exist</returns>
		/// <exception cref="System.ArgumentException">If the key is null or empty</exception>
		/// <exception cref="System.OperationCanceledException">If the cancellation token is already triggered</exception>
		/// <exception cref="System.ObjectDisposedException">If the transaction has already been completed</exception>
		/// <exception cref="System.InvalidOperationException">If the operation method is called from the Network Thread</exception>
		public byte[] Get(byte[] key, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return Get(new ArraySegment<byte>(key), snapshot, ct);
		}
#endif

		public ArraySegment<byte> Get(IFdbKey key, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return Get(key.ToBytes(), snapshot, ct);
		}

		/// <summary>Returns the value of a particular key</summary>
		/// <param name="keyBytes">Key to retrieve (UTF-8)</param>
		/// <param name="snapshot"></param>
		/// <param name="ct">CancellationToken used to cancel this operation</param>
		/// <returns>Returns the value of the key if it is found, or null if the key does not exist</returns>
		/// <exception cref="System.ArgumentException">If the key is null or empty</exception>
		/// <exception cref="System.OperationCanceledException">If the cancellation token is already triggered</exception>
		/// <exception cref="System.ObjectDisposedException">If the transaction has already been completed</exception>
		/// <exception cref="System.InvalidOperationException">If the operation method is called from the Network Thread</exception>
		public ArraySegment<byte> Get(ArraySegment<byte> keyBytes, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			ThrowIfDisposed();
			ct.ThrowIfCancellationRequested();
			Fdb.EnsureNotOnNetworkThread();

			return GetCore(keyBytes, snapshot, ct);
		}

		public Task<List<KeyValuePair<IFdbKey, ArraySegment<byte>>>> GetBatchAsync(IEnumerable<IFdbKey> keys, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			ct.ThrowIfCancellationRequested();
			return GetBatchAsync(keys.ToArray(), snapshot, ct);
		}

		public async Task<List<KeyValuePair<IFdbKey, ArraySegment<byte>>>> GetBatchAsync(IFdbKey[] keys, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			ThrowIfDisposed();

			ct.ThrowIfCancellationRequested();

			Fdb.EnsureNotOnNetworkThread();

			var tasks = new List<Task<ArraySegment<byte>>>(keys.Length);
			for (int i = 0; i < keys.Length; i++)
			{
				//TODO: optimize to not have to allocate a scope
				tasks.Add(Task.Factory.StartNew((_state) =>
				{
					return this.GetCoreAsync(keys[(int)_state].ToBytes(), snapshot, ct);
				}, i, ct).Unwrap());
			}

			var results = await Task.WhenAll(tasks);

			return results
				.Select((data, i) => new KeyValuePair<IFdbKey, ArraySegment<byte>>(keys[i], data))
				.ToList();
		}

		#endregion

		#region GetRange...

		private static KeyValuePair<ArraySegment<byte>, ArraySegment<byte>>[] GetKeyValueArrayResult(FutureHandle h, out bool more)
		{
			KeyValuePair<ArraySegment<byte>, ArraySegment<byte>>[] result;
			var err = FdbNative.FutureGetKeyValueArray(h, out result, out more);
#if DEBUG_TRANSACTIONS
			Debug.WriteLine("FdbTransaction[].GetKeyValueArrayResult() => err=" + err + ", result=" + (result != null ? result.Length : -1) + ", more=" + more);
#endif
			Fdb.DieOnError(err);
			return result;
		}

		internal Task<FdbSearchResults> GetRangeCoreAsync(FdbKeySelector begin, FdbKeySelector end, int limit, int targetBytes, FdbStreamingMode mode, int iteration, bool snapshot, bool reverse, CancellationToken ct)
		{
			Fdb.EnsureKeyIsValid(begin.Key);
			Fdb.EnsureKeyIsValid(end.Key);

			var searchResults = new FdbSearchResults
			{
				Begin = begin,
				End = end,
				Reverse = reverse,
				Iteration = iteration,
			};

			var future = FdbNative.TransactionGetRange(m_handle, begin, end, limit, targetBytes, mode, iteration, snapshot, reverse);
			return FdbFuture.CreateTaskFromHandle(
				future,
				(h) =>
				{
					bool hasMore;
					searchResults.Page = GetKeyValueArrayResult(h, out hasMore);
					searchResults.HasMore = hasMore;
					return searchResults;
				},
				ct
			);
		}

		public Task<FdbSearchResults> GetRangeInclusiveAsync(FdbKeySelector beginInclusive, FdbKeySelector endInclusive, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return GetRangeAsync(beginInclusive, endInclusive + 1, snapshot, ct);
		}

		public Task<FdbSearchResults> GetRangeAsync(IFdbKey beginInclusive, IFdbKey endExclusive, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			return GetRangeAsync(
				FdbKeySelector.FirstGreaterOrEqual(beginInclusive),
				FdbKeySelector.FirstGreaterThan(endExclusive),
				snapshot,
				ct
			);
			
		}

		public Task<FdbSearchResults> GetRangeAsync(FdbKeySelector beginInclusive, FdbKeySelector endExclusive, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			ct.ThrowIfCancellationRequested();
			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			return GetRangeCoreAsync(beginInclusive, endExclusive, limit: 0, targetBytes: 0, mode: FdbStreamingMode.WantAll, iteration: 0, snapshot: snapshot, reverse: false, ct: ct);
		}

		public Task<FdbSearchResults> GetRangeAsync(FdbKeySelector beginInclusive, FdbKeySelector endExclusive, int limit, int targetBytes, FdbStreamingMode mode, int iteration, bool snapshot, bool reverse, CancellationToken ct = default(CancellationToken))
		{
			ct.ThrowIfCancellationRequested();
			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			return GetRangeCoreAsync(beginInclusive, endExclusive, limit, targetBytes, mode, iteration, snapshot, reverse, ct);
		}

		#endregion

		#region GetKey...

		private static ArraySegment<byte> GetKeyResult(FutureHandle h)
		{
			ArraySegment<byte> result;
			var err = FdbNative.FutureGetKey(h, out result);
#if DEBUG_TRANSACTIONS
			Debug.WriteLine("FdbTransaction[].GetKeyResult() => err=" + err + ", result=" + FdbKey.Dump(result));
#endif
			Fdb.DieOnError(err);
			return result;
		}

		internal Task<ArraySegment<byte>> GetKeyCoreAsync(FdbKeySelector selector, bool snapshot, CancellationToken ct)
		{
			Fdb.EnsureKeyIsValid(selector.Key);

			var future = FdbNative.TransactionGetKey(m_handle, selector, snapshot);
			return FdbFuture.CreateTaskFromHandle(
				future,
				(h) =>
				{
					return GetKeyResult(h);
				},
				ct
			);
		}

		public Task<ArraySegment<byte>> GetKeyAsync(FdbKeySelector selector, bool snapshot = false, CancellationToken ct = default(CancellationToken))
		{
			ct.ThrowIfCancellationRequested();
			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			return GetKeyCoreAsync(selector, snapshot, ct);			
		}

		#endregion

		#region Set...

		internal void SetCore(ArraySegment<byte> key, ArraySegment<byte> value)
		{
			Fdb.EnsureKeyIsValid(key);
			Fdb.EnsureValueIsValid(value);

			FdbNative.TransactionSet(m_handle, key, value);
			Interlocked.Add(ref m_payloadBytes, key.Count + value.Count);
		}

		public void Set(ArraySegment<byte> keyBytes, ArraySegment<byte> valueBytes)
		{
			ThrowIfDisposed();
			//Fdb.EnsureNotOnNetworkThread();

			SetCore(keyBytes, valueBytes);
		}

		public void Set(IFdbKey key, byte[] valueBytes)
		{
			if (key == null) throw new ArgumentNullException("key");
			if (valueBytes == null) throw new ArgumentNullException("valueBytes");

			ThrowIfDisposed();
			//Fdb.EnsureNotOnNetworkThread();

			SetCore(key.ToBytes(), new ArraySegment<byte>(valueBytes));
		}

		public void Set(IFdbKey key, ArraySegment<byte> valueBytes)
		{
			if (key == null) throw new ArgumentNullException("key");

			ThrowIfDisposed();
			//Fdb.EnsureNotOnNetworkThread();

			SetCore(key.ToBytes(), valueBytes);
		}

#if DEPRECATED

		public void Set(string key, byte[] value)
		{
			if (key == null) throw new ArgumentNullException("key");
			if (value == null) throw new ArgumentNullException("value");

			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			SetCore(Fdb.GetKeyBytes(key), new ArraySegment<byte>(value));
		}

		public void Set(string key, string value)
		{
			if (key == null) throw new ArgumentNullException("key");
			if (value == null) throw new ArgumentNullException("value");

			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			SetCore(Fdb.GetKeyBytes(key), Fdb.GetValueBytes(value));
		}

		public void Set(string key, long value)
		{
			if (key == null) throw new ArgumentNullException("key");

			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			//HACKHACK: use something else! (endianness depends on plateform)
			var bytes = BitConverter.GetBytes(value);
			//TODO: only stores the needed number of bytes ?
			//or use ZigZag encoding ?
			SetCore(Fdb.GetKeyBytes(key), new ArraySegment<byte>(bytes));
		}

#endif

		#endregion

		#region Clear...

		internal void ClearCore(ArraySegment<byte> key)
		{
			Fdb.EnsureKeyIsValid(key);

			FdbNative.TransactionClear(m_handle, key);
			Interlocked.Add(ref m_payloadBytes, key.Count);
		}

		public void Clear(ArraySegment<byte> key)
		{
			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			ClearCore(key);
		}

		public void Clear(byte[] key)
		{
			if (key == null) throw new ArgumentNullException("key");

			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			ClearCore(new ArraySegment<byte>(key));
		}

		public void Clear(string key)
		{
			if (key == null) throw new ArgumentNullException("key");

			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			ClearCore(Fdb.GetKeyBytes(key));
		}

		#endregion

		#region Clear...

		internal void ClearRangeCore(ArraySegment<byte> beginKeyInclusive, ArraySegment<byte> endKeyExclusive)
		{
			Fdb.EnsureKeyIsValid(beginKeyInclusive);
			Fdb.EnsureKeyIsValid(endKeyExclusive);

			FdbNative.TransactionClear(m_handle, beginKeyInclusive);
			//TODO: how to account for these ?
			//Interlocked.Add(ref m_payloadBytes, beginKey.Count);
			//Interlocked.Add(ref m_payloadBytes, endKey.Count);
		}

		/// <summary>
		/// Modify the database snapshot represented by transaction to remove all keys (if any) which are lexicographically greater than or equal to the given begin key and lexicographically less than the given end_key.
		/// Sets and clears affect the actual database only if transaction is later committed with fdb_transaction_commit().
		/// </summary>
		/// <param name="beginKeyInclusive"></param>
		/// <param name="endKeyExclusive"></param>
		public void ClearRange(ArraySegment<byte> beginKeyInclusive, ArraySegment<byte> endKeyExclusive)
		{
			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			ClearRangeCore(beginKeyInclusive, endKeyExclusive);
		}

		public void ClearRange(IFdbKey beginInclusive, IFdbKey endExclusive)
		{
			if (beginInclusive == null) throw new ArgumentNullException("beginInclusive");
			if (endExclusive == null) throw new ArgumentNullException("endExclusive");

			ThrowIfDisposed();
			Fdb.EnsureNotOnNetworkThread();

			ClearRangeCore(beginInclusive.ToBytes(), endExclusive.ToBytes());
		}

		#endregion

		#region Commit...

		public Task CommitAsync(CancellationToken ct = default(CancellationToken))
		{
			ThrowIfDisposed();

			ct.ThrowIfCancellationRequested();

			Fdb.EnsureNotOnNetworkThread();

			var future = FdbNative.TransactionCommit(m_handle);
			return FdbFuture.CreateTaskFromHandle<object>(future, (h) => null, ct);
		}

		public void Commit(CancellationToken ct = default(CancellationToken))
		{
			ThrowIfDisposed();

			ct.ThrowIfCancellationRequested();

			Fdb.EnsureNotOnNetworkThread();

			FutureHandle handle = null;
			try
			{
				// calls fdb_transaction_commit
				handle = FdbNative.TransactionCommit(m_handle);
				using (var future = FdbFuture.FromHandle<object>(handle, (h) => null, ct, willBlockForResult: true))
				{
					future.Wait();
				}
			}
			catch (Exception)
			{
				if (handle != null) handle.Dispose();
			}
		}

		#endregion

		#region Reset/Rollback...

		/// <summary>Reset the transaction to its initial state.</summary>
		public void Reset()
		{
			ThrowIfDisposed();

			Fdb.EnsureNotOnNetworkThread();

			FdbNative.TransactionReset(m_handle);
		}

		/// <summary>Rollback this transaction, and dispose it. It should not be used after that.</summary>
		public void Rollback()
		{
			//TODO: refactor code between Rollback() and Dispose() ?
			this.Dispose();
		}

		#endregion

		private void ThrowIfDisposed()
		{
			if (m_disposed) throw new ObjectDisposedException(null);
			// also checks that the DB has not been disposed behind our back
			m_database.EnsureCheckTransactionIsValid(this);
		}

		public void Dispose()
		{
			// note: we can be called by user code, or by the FdbDatabase when it is terminating with pending transactions
			if (!m_disposed)
			{
				m_disposed = true;

				try
				{
					m_database.UnregisterTransaction(this);
				}
				finally
				{
					m_handle.Dispose();
				}
			}
		}
	}

}
