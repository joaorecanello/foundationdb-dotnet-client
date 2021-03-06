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
	* Neither the name of Doxense nor the
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

namespace FoundationDB.Client
{
	using FoundationDB.Async;
	using FoundationDB.Linq;
	using System;
	using System.Collections.Generic;
	using System.Linq;

	public static class FdbMergeQueryExtensions
	{

		#region MergeSort (x OR y)

		public static IFdbAsyncEnumerable<KeyValuePair<Slice, Slice>> MergeSort<TKey>(this IFdbReadOnlyTransaction trans, IEnumerable<FdbKeySelectorPair> ranges, Func<KeyValuePair<Slice, Slice>, TKey> keySelector, IComparer<TKey> keyComparer = null)
		{
			//TODO: Range options ?

			trans.EnsureCanRead();
			return new FdbMergeSortIterator<KeyValuePair<Slice, Slice>, TKey, KeyValuePair<Slice, Slice>>(
				ranges.Select(range => trans.GetRange(range, new FdbRangeOptions { Mode = FdbStreamingMode.Iterator })),
				default(int?),
				keySelector,
				TaskHelpers.Cache<KeyValuePair<Slice, Slice>>.Identity,
				keyComparer
			);
		}

		public static IFdbAsyncEnumerable<TResult> MergeSort<TKey, TResult>(this IFdbReadOnlyTransaction trans, IEnumerable<FdbKeySelectorPair> ranges, Func<KeyValuePair<Slice, Slice>, TKey> keySelector, Func<KeyValuePair<Slice, Slice>, TResult> resultSelector, IComparer<TKey> keyComparer = null)
		{
			//TODO: Range options ?

			trans.EnsureCanRead();
			return new FdbMergeSortIterator<KeyValuePair<Slice, Slice>, TKey, TResult>(
				ranges.Select(range => trans.GetRange(range, new FdbRangeOptions { Mode = FdbStreamingMode.Iterator })),
				default(int?),
				keySelector,
				resultSelector,
				keyComparer
			);
		}

		public static IFdbAsyncEnumerable<TResult> Union<TKey, TResult>(IEnumerable<IFdbAsyncEnumerable<TResult>> sources, Func<TResult, TKey> keySelector, IComparer<TKey> keyComparer = null)
		{
			return new FdbMergeSortIterator<TResult, TKey, TResult>(
				sources,
				null,
				keySelector,
				TaskHelpers.Cache<TResult>.Identity,
				keyComparer
			);
		}

		public static IFdbAsyncEnumerable<TResult> Union<TResult>(IEnumerable<IFdbAsyncEnumerable<TResult>> sources, IComparer<TResult> keyComparer = null)
		{
			return new FdbMergeSortIterator<TResult, TResult, TResult>(
				sources,
				null,
				TaskHelpers.Cache<TResult>.Identity,
				TaskHelpers.Cache<TResult>.Identity,
				keyComparer
			);
		}

		#endregion

		#region Intersect (x AND y)

		public static IFdbAsyncEnumerable<KeyValuePair<Slice, Slice>> Intersect<TKey>(this IFdbReadOnlyTransaction trans, IEnumerable<FdbKeySelectorPair> ranges, Func<KeyValuePair<Slice, Slice>, TKey> keySelector, IComparer<TKey> keyComparer = null)
		{
			//TODO: Range options ?

			trans.EnsureCanRead();
			return new FdbIntersectIterator<KeyValuePair<Slice, Slice>, TKey, KeyValuePair<Slice, Slice>>(
				ranges.Select(range => trans.GetRange(range, new FdbRangeOptions { Mode = FdbStreamingMode.Iterator })),
				default(int?),
				keySelector,
				TaskHelpers.Cache<KeyValuePair<Slice, Slice>>.Identity,
				keyComparer
			);
		}

		public static IFdbAsyncEnumerable<TResult> Intersect<TKey, TResult>(this IFdbReadOnlyTransaction trans, IEnumerable<FdbKeySelectorPair> ranges, Func<KeyValuePair<Slice, Slice>, TKey> keySelector, Func<KeyValuePair<Slice, Slice>, TResult> resultSelector, IComparer<TKey> keyComparer = null)
		{
			//TODO: Range options ?

			trans.EnsureCanRead();
			return new FdbIntersectIterator<KeyValuePair<Slice, Slice>, TKey, TResult>(
				ranges.Select(range => trans.GetRange(range, new FdbRangeOptions { Mode = FdbStreamingMode.Iterator })),
				default(int?),
				keySelector,
				resultSelector,
				keyComparer
			);
		}

		public static IFdbAsyncEnumerable<TResult> Intersect<TKey, TResult>(this IFdbAsyncEnumerable<TResult> first, IFdbAsyncEnumerable<TResult> second, Func<TResult, TKey> keySelector, IComparer<TKey> keyComparer = null)
		{
			return new FdbIntersectIterator<TResult, TKey, TResult>(
				new[] { first, second },
				null,
				keySelector,
				TaskHelpers.Cache<TResult>.Identity,
				keyComparer
			);
		}

		public static IFdbAsyncEnumerable<TResult> Intersect<TResult>(this IFdbAsyncEnumerable<TResult> first, IFdbAsyncEnumerable<TResult> second, IComparer<TResult> comparer = null)
		{
			return new FdbIntersectIterator<TResult, TResult, TResult>(
				new [] { first, second },
				null,
				TaskHelpers.Cache<TResult>.Identity,
				TaskHelpers.Cache<TResult>.Identity,
				comparer
			);
		}

		public static IFdbAsyncEnumerable<TResult> Intersect<TKey, TResult>(IEnumerable<IFdbAsyncEnumerable<TResult>> sources, Func<TResult, TKey> keySelector, IComparer<TKey> keyComparer = null)
		{
			return new FdbIntersectIterator<TResult, TKey, TResult>(
				sources,
				null,
				keySelector,
				TaskHelpers.Cache<TResult>.Identity,
				keyComparer
			);
		}

		public static IFdbAsyncEnumerable<TResult> Intersect<TResult>(IEnumerable<IFdbAsyncEnumerable<TResult>> sources, IComparer<TResult> keyComparer = null)
		{
			return new FdbIntersectIterator<TResult, TResult, TResult>(
				sources,
				null,
				TaskHelpers.Cache<TResult>.Identity,
				TaskHelpers.Cache<TResult>.Identity,
				keyComparer
			);
		}

		#endregion

		#region Except (x AND NOT y)

		/// <summary>Return the keys that are in the first range, but not in the others</summary>
		/// <typeparam name="TKey">Type of the keys returned by the query</typeparam>
		/// <param name="trans">Transaction used by the operation</param>
		/// <param name="ranges">List of at least one key selector pairs</param>
		/// <param name="keySelector">Lambda called to extract the keys from the ranges</param>
		/// <param name="keyComparer">Instance used to compare the keys returned by <paramref name="keySelector"/></param>
		/// <returns>Async query that returns only the results that are in the first range, and not in any other range.</returns>
		public static IFdbAsyncEnumerable<KeyValuePair<Slice, Slice>> Except<TKey>(this IFdbReadOnlyTransaction trans, IEnumerable<FdbKeySelectorPair> ranges, Func<KeyValuePair<Slice, Slice>, TKey> keySelector, IComparer<TKey> keyComparer = null)
		{
			//TODO: Range options ?
			if (trans == null) throw new ArgumentNullException("trans");
			if (ranges == null) throw new ArgumentNullException("ranges");
			if (keySelector == null) throw new ArgumentNullException("keySelector");

			trans.EnsureCanRead();
			return new FdbExceptIterator<KeyValuePair<Slice, Slice>, TKey, KeyValuePair<Slice, Slice>>(
				ranges.Select(range => trans.GetRange(range, new FdbRangeOptions { Mode = FdbStreamingMode.Iterator })),
				default(int?),
				keySelector,
				TaskHelpers.Cache<KeyValuePair<Slice, Slice>>.Identity,
				keyComparer
			);
		}

		/// <summary>Return the keys that are in the first range, but not in the others</summary>
		/// <typeparam name="TKey">Type of the keys returned by the query</typeparam>
		/// <param name="trans">Transaction used by the operation</param>
		/// <param name="ranges">List of at least one key range</param>
		/// <param name="keySelector">Lambda called to extract the keys from the ranges</param>
		/// <param name="keyComparer">Instance used to compare the keys returned by <paramref name="keySelector"/></param>
		/// <returns>Async query that returns only the results that are in the first range, and not in any other range.</returns>
		public static IFdbAsyncEnumerable<KeyValuePair<Slice, Slice>> Except<TKey>(this IFdbReadOnlyTransaction trans, IEnumerable<FdbKeyRange> ranges, Func<KeyValuePair<Slice, Slice>, TKey> keySelector, IComparer<TKey> keyComparer = null)
		{
			if (ranges == null) throw new ArgumentNullException("ranges");
			return Except<TKey>(trans, ranges.Select(r => FdbKeySelectorPair.Create(r)), keySelector, keyComparer);
		}

		/// <summary>Return the keys that are in the first range, but not in the others</summary>
		/// <typeparam name="TKey">Type of the keys used for the comparison</typeparam>
		/// <typeparam name="TResult">Type of the results returned by the query</typeparam>
		/// <param name="trans">Transaction used by the operation</param>
		/// <param name="ranges">List of at least one key selector pairs</param>
		/// <param name="keySelector">Lambda called to extract the keys used by the sort</param>
		/// <param name="resultSelector">Lambda called to extract the values returned by the query</param>
		/// <param name="keyComparer">Instance used to compare the keys returned by <paramref name="keySelector"/></param>
		/// <returns>Async query that returns only the results that are in the first range, and not in any other range.</returns>
		public static IFdbAsyncEnumerable<TResult> Except<TKey, TResult>(this IFdbReadOnlyTransaction trans, IEnumerable<FdbKeySelectorPair> ranges, Func<KeyValuePair<Slice, Slice>, TKey> keySelector, Func<KeyValuePair<Slice, Slice>, TResult> resultSelector, IComparer<TKey> keyComparer = null)
		{
			//TODO: Range options ?

			trans.EnsureCanRead();
			return new FdbExceptIterator<KeyValuePair<Slice, Slice>, TKey, TResult>(
				ranges.Select(range => trans.GetRange(range, new FdbRangeOptions { Mode = FdbStreamingMode.Iterator })),
				default(int?),
				keySelector,
				resultSelector,
				keyComparer
			);
		}

		/// <summary>Return the keys that are in the first range, but not in the others</summary>
		/// <typeparam name="TKey">Type of the keys used for the comparison</typeparam>
		/// <typeparam name="TResult">Type of the results returned by the query</typeparam>
		/// <param name="trans">Transaction used by the operation</param>
		/// <param name="ranges">List of at least one key ranges</param>
		/// <param name="keySelector">Lambda called to extract the keys used by the sort</param>
		/// <param name="resultSelector">Lambda called to extract the values returned by the query</param>
		/// <param name="keyComparer">Instance used to compare the keys returned by <paramref name="keySelector"/></param>
		/// <returns>Async query that returns only the results that are in the first range, and not in any other range.</returns>
		public static IFdbAsyncEnumerable<TResult> Except<TKey, TResult>(this IFdbReadOnlyTransaction trans, IEnumerable<FdbKeyRange> ranges, Func<KeyValuePair<Slice, Slice>, TKey> keySelector, Func<KeyValuePair<Slice, Slice>, TResult> resultSelector, IComparer<TKey> keyComparer = null)
		{
			if (ranges == null) throw new ArgumentNullException("ranges");
			return Except<TKey, TResult>(trans, ranges.Select(r => FdbKeySelectorPair.Create(r)), keySelector, resultSelector, keyComparer);
		}

		/// <summary>Sequence the return only the elements of <paramref name="first"/> that are not in <paramref name="second"/>, using a custom key comparison</summary>
		/// <typeparam name="TKey">Type of the keys that will be used for comparison</typeparam>
		/// <typeparam name="TResult">Type of the results of the query</typeparam>
		/// <param name="first">Fisrt query that contains the elements that could be in the result</param>
		/// <param name="second">Second query that contains the elements that cannot be in the result</param>
		/// <param name="keySelector">Lambda used to extract keys from both queries.</param>
		/// <param name="keyComparer">Instance used to compare keys</param>
		/// <returns>Async query that returns only the elements that are in <paramref name="first"/>, and not in <paramref name="second"/></returns>
		public static IFdbAsyncEnumerable<TResult> Except<TKey, TResult>(this IFdbAsyncEnumerable<TResult> first, IFdbAsyncEnumerable<TResult> second, Func<TResult, TKey> keySelector, IComparer<TKey> keyComparer = null)
		{
			return new FdbExceptIterator<TResult, TKey, TResult>(
				new[] { first, second },
				null,
				keySelector,
				TaskHelpers.Cache<TResult>.Identity,
				keyComparer
			);
		}

		/// <summary>Sequence the return only the elements of <paramref name="first"/> that are not in <paramref name="second"/></summary>
		/// <typeparam name="TResult">Type of the results of the query</typeparam>
		/// <param name="first">Fisrt query that contains the elements that could be in the result</param>
		/// <param name="second">Second query that contains the elements that cannot be in the result</param>
		/// <param name="comparer">Instance used to compare elements</param>
		/// <returns>Async query that returns only the elements that are in <paramref name="first"/>, and not in <paramref name="second"/></returns>
		public static IFdbAsyncEnumerable<TResult> Except<TResult>(this IFdbAsyncEnumerable<TResult> first, IFdbAsyncEnumerable<TResult> second, IComparer<TResult> comparer = null)
		{
			return new FdbExceptIterator<TResult, TResult, TResult>(
				new[] { first, second },
				null,
				TaskHelpers.Cache<TResult>.Identity,
				TaskHelpers.Cache<TResult>.Identity,
				comparer
			);
		}

		#endregion

	}

}
