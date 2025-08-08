set max_parallel_workers_per_gather = 2;


CREATE UNLOGGED TABLE payments (
    correlation_id uuid primary key,
    amount DECIMAL,
    processor int,
    requested_at TIMESTAMPTZ
);

CREATE INDEX payments_requested_at ON payments (requested_at);
CREATE INDEX payments_requested_at_processor ON payments (requested_at, processor);