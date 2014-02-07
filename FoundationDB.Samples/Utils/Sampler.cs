﻿//TODO: License for samples/tutorials ???

namespace FoundationDB.Samples.Benchmarks
{
	using FoundationDB.Client;
	using FoundationDB.Client.Utils;
	using FoundationDB.Layers.Tuples;
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.IO;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;
	using Doxense.Mathematics.Statistics;
	using System.Diagnostics;

	public class SamplerTest : IAsyncTest
	{

		public SamplerTest(double ratio)
		{
			this.Ratio = ratio;
		}

		public double Ratio { get; private set; }

		#region IAsyncTest...

		public string Name { get { return "SamplerTest"; } }

		private static string FormatSize(long size)
		{
			if (size < 10000) return size.ToString("N0");
			double x = size / 1024.0;
			if (x < 800) return x.ToString("N1") + " kB";
			x /= 1024.0;
			if (x < 800) return x.ToString("N2") + " MB";
			x /= 1024.0;
			return x.ToString("N2") + " GB";
		}

		public async Task Run(FdbDatabasePartition db, TextWriter log, CancellationToken ct)
		{
			Console.WriteLine("# Sampler test initialized");

			var keyServers = Slice.FromAscii("\xFF/keyServers/");

			// dump keyServers
			var shards = await db.QueryAsync(tr => tr.WithAccessToSystemKeys()
				.GetRange(FdbKeyRange.StartsWith(keyServers))
				.Select(kvp => kvp.Key.Substring(keyServers.Count))
				.Where(key => key < FdbKey.MaxValue)
			);
			Console.WriteLine("Found " + shards.Count + " shards:");

			var ranges = shards.Zip(shards.Concat(new[] { Slice.FromAscii("\xFF") }).Skip(1), (x, y) => FdbKeyRange.Create(x, y)).ToList();

			// take a sample
			var rnd = new Random();
			int sz = Math.Max((int)Math.Ceiling(this.Ratio * ranges.Count), 1);
			if (sz > 100) sz = 100; //SAFETY
			var samples = new List<FdbKeyRange>();
			for (int i = 0; i < sz; i++)
			{
				int p = rnd.Next(ranges.Count);
				samples.Add(ranges[p]);
				ranges.RemoveAt(p);
			}

			Console.WriteLine("Sampling " + (100 * Ratio).ToString("N0") + "% of " + samples.Count + " shards...");

			Console.WriteLine("{0,9}{1,10}{2,10}{3,10}", "Count", "Keys", "Values", "Total");

			var rangeOptions = new FdbRangeOptions { Mode = FdbStreamingMode.WantAll };

			samples = samples.OrderBy(x => x.Begin).ToList();

			long total = 0;

			var sw = Stopwatch.StartNew();
			var tasks = new List<Task>();
			while(samples.Count > 0)
			{
				while(tasks.Count < 6 && samples.Count > 0)
				{
					var range = samples[0];
					samples.RemoveAt(0);
					tasks.Add(Task.Run(async () =>
					{
						var hh = new RobustHistogram(RobustHistogram.TimeScale.Ticks);

						using (var tr = db.BeginTransaction())
						{
							long keySize = 0;
							long valueSize = 0;
							long count = 0;

							int iter = 0;
							var beginSelector = FdbKeySelector.FirstGreaterOrEqual(range.Begin);
							var endSelector = FdbKeySelector.FirstGreaterOrEqual(range.End);
							while (true)
							{
								FdbRangeChunk data = default(FdbRangeChunk);
								FdbException error = null;
								try
								{

									data = await tr.Snapshot.GetRangeAsync(
										beginSelector,
										endSelector,
										rangeOptions,
										iter
									).ConfigureAwait(false);
								}
								catch (FdbException e)
								{
									error = e;
								}

								if (error != null)
								{
									await tr.OnErrorAsync(error.Code).ConfigureAwait(false);
									continue;
								}

								if (data.Count == 0) break;

								count += data.Count;
								foreach (var kvp in data.Chunk)
								{
									keySize += kvp.Key.Count;
									valueSize += kvp.Value.Count;

									hh.Add(TimeSpan.FromTicks(kvp.Key.Count + kvp.Value.Count));
								}

								if (!data.HasMore) break;

								beginSelector = FdbKeySelector.FirstGreaterThan(data.Last.Key);
								++iter;
							}

							long totalSize = keySize + valueSize;
							Interlocked.Add(ref total, totalSize);

							Console.WriteLine("{0,9}{1,10}{2,10}{3,10} : {4}", count.ToString("N0"), FormatSize(keySize), FormatSize(valueSize), FormatSize(totalSize), hh.GetDistribution(begin: 1, end: 10000, fold:2));
						}
					}));
				}

				var done = await Task.WhenAny(tasks);
				tasks.Remove(done);
			}

			await Task.WhenAll(tasks);
			sw.Stop();

			Console.WriteLine("> Sampled " + FormatSize(total) + " (" + total.ToString("N0") + " bytes) in " + sw.Elapsed.TotalSeconds.ToString("N1") + " sec");
			Console.WriteLine("> Estimated total size is " + FormatSize(total * shards.Count / sz));
		}

		#endregion

	}
}
