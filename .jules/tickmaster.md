## 2024-05-20 - Stagger TickManager logic in StoryOrchestrator
**Action:** Staggered `StoryLogTracker.PollNativeLogs` and `StoryLogTracker.PollExternalLogs` to fire on separate ticks (`% 2000 == 0` and `% 2000 == 1000`) instead of running both on the same tick.
**Learning:** Polling logic inside `GameComponentTick` was causing both native and external log aggregations to run on the exact same frame every 2000 ticks. Staggering them reduces CPU spikes on that particular frame.
