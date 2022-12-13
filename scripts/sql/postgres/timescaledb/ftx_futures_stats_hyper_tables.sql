CREATE OR REPLACE FUNCTION current_time_millisecond() RETURNS BIGINT
    LANGUAGE SQL STABLE AS $$
select (extract(epoch from now()) * 1000)::BIGINT
$$;

ALTER TABLE "ftx_futures_stats" DROP CONSTRAINT IF EXISTS ftx_futures_stats_pkey ;

SELECT create_hypertable('ftx_futures_stats', 'time',
    partitioning_column => 'market_hash',
    number_partitions => 8,
    chunk_time_interval => 32::bigint * 24::bigint * 60::bigint * 60::bigint * 1000::bigint,
    if_not_exists => true,
    migrate_data => true
    );

CREATE UNIQUE INDEX IF NOT EXISTS ftx_futures_stats_id_index
    ON ftx_futures_stats USING btree
        (id DESC NULLS LAST, time DESC, market_hash ASC)
;

SELECT set_integer_now_func('ftx_futures_stats', 'current_time_millisecond', true);

ALTER TABLE IF EXISTS ftx_futures_stats SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'market_hash'
);

SELECT add_compression_policy('ftx_futures_stats', 32::bigint * 24::bigint * 60::bigint * 60::bigint * 1000::bigint + 1, if_not_exists := true);