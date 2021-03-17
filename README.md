Spike/PoC simplified repository to check possible implementation of database-backed event/job store:
- Models an "Application", consisting of N "Producers" and M "Consumers"
- There can be multiple co-existing Application instances
- Producer generates an event and inserts it into a database
- Consumer reacts to in-app "event produced" signals, and also performs interval-based database polling (in case event is produced by another Application instance)
    - Can be optimized by app-to-app signal broadcasting, if needed

# Observed results

## Case 1

Application 1:
  - 10 producers
    - Produce interval: 300-1000ms
  - 1 consumer
    - Polling interval: 3000ms
    - Event handling duration: 10ms
    - Delay before picking next job: 0ms

Application 2:
  - 5 producers
    - Produce interval: 300-1000ms
  - no consumers

*Latency in milliseconds:*

create_consume_latency (same_app=True): p50=113, p99=268
create_consume_latency (same_app=False): p50=99, p99=256
insert_consume_latency (same_app=True): p50=106, p99=259
insert_consume_latency (same_app=False): p50=92, p99=255
