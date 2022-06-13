CREATE OR REPLACE FUNCTION ftx.current_time_millisecond() RETURNS BIGINT
    LANGUAGE SQL STABLE AS $$
select (extract(epoch from now()) * 1000)::BIGINT
$$;

ALTER TABLE ftx."ftx_trade_btc_usd" DROP CONSTRAINT IF EXISTS ftx_trade_btc_usd_pkey;

SELECT create_hypertable('ftx."ftx_trade_btc_usd"', 'time',
    chunk_time_interval => 30::bigint * 24::bigint * 60::bigint * 60::bigint * 1000::bigint,
    if_not_exists => true,
    migrate_data => true
    );

SELECT set_integer_now_func('ftx."ftx_trade_btc_usd"', 'ftx.current_time_millisecond', true);

ALTER TABLE ftx."ftx_trade_btc_usd" SET (
    timescaledb.compress
);

--SELECT add_compression_policy('ftx."ftx_trade_btc_usd"', 128::bigint * 24::bigint * 60::bigint * 60::bigint * 1000::bigint + 1, if_not_exists := true);