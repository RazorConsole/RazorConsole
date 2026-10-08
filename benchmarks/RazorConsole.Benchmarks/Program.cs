// Copyright (c) RazorConsole. All rights reserved.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// In-process: the repo-wide Directory.Build.props multi-targets every project under the repo
// (including BenchmarkDotNet's generated one), which breaks out-of-process builds.
var config = DefaultConfig.Instance.AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));

BenchmarkSwitcher.FromAssembly(typeof(RazorConsole.Benchmarks.RenderingBenchmarks).Assembly).Run(args, config);
