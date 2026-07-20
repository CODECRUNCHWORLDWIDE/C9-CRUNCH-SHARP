# Week 4 — Challenges

The exercises drill basics. **Challenges stretch you.** This week's challenge takes ~2 hours and produces something you can commit to your portfolio: a rate-limited HTTP fetcher built on `Channel<TimeSlot>` that obeys an explicit requests-per-second budget across multiple concurrent consumers — with `BenchmarkDotNet` measurements proving the rate limiter actually works under load.

## Index

1. **[Challenge 1 — Build a rate-limited fetcher](challenge-01-build-a-rate-limited-fetcher.md)** — design a token-bucket-style rate limiter using a `Channel<TimeSlot>` and a refill loop; wire it as a decorator around `HttpClient`; prove with `BenchmarkDotNet` that you stay under 10 req/sec across 8 concurrent callers. (~120 min)

Challenges are optional. If you skip them, you can still pass the week. If you do them, you'll be measurably ahead — the rate-limiting pattern you build here is the same one Polly's `RateLimiter` policy and `System.Threading.RateLimiting` use under the hood, and you'll spot the design in every production HTTP gateway from Week 8 onward.
