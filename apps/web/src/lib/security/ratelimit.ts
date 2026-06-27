// Sabit pencere rate limiter — .NET RateLimiter ile aynı mantık. Fail-closed.
export interface RateLimitResult {
  allowed: boolean;
  remaining: number;
  retrySeconds: number;
}

export class RateLimiter {
  private state = new Map<string, { count: number; windowStart: number }>();
  private readonly max: number;
  private readonly windowMs: number;
  private readonly now: () => number;

  constructor(max: number, windowMs: number, now: () => number = () => Date.now()) {
    this.max = max;
    this.windowMs = windowMs;
    this.now = now;
  }

  static login(now?: () => number): RateLimiter {
    return new RateLimiter(5, 5 * 60_000, now);
  }
  static syncPush(now?: () => number): RateLimiter {
    return new RateLimiter(60, 60_000, now);
  }
  static admin(now?: () => number): RateLimiter {
    return new RateLimiter(30, 60_000, now);
  }

  check(key: string): RateLimitResult {
    const now = this.now();
    let e = this.state.get(key);
    if (!e || now - e.windowStart >= this.windowMs) e = { count: 0, windowStart: now };
    if (e.count >= this.max) {
      const retry = Math.ceil((e.windowStart + this.windowMs - now) / 1000);
      return { allowed: false, remaining: 0, retrySeconds: Math.max(retry, 1) };
    }
    e.count += 1;
    this.state.set(key, e);
    return { allowed: true, remaining: this.max - e.count, retrySeconds: 0 };
  }

  reset(key: string): void {
    this.state.delete(key);
  }
}
