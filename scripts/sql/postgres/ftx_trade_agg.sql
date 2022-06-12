BEGIN;

CREATE TABLE IF NOT EXISTS ftx.ftx_trade_agg_template
(
    "time" bigint NOT NULL,
    "open" real NOT NULL,
    "high" real NOT NULL,
    "low" real NOT NULL,
    "close" real NOT NULL,
    "volume" real NOT NULL,
    
    "mean_price" real NOT NULL,
    "std_price" real NOT NULL,
    "kurtosis_price" real NOT NULL,
    "skewness_price" real NOT NULL,
    "ema_price" real NOT NULL,
    
    "mean_volume" real NOT NULL,
    "std_volume" real NOT NULL,
    "kurtosis_volume" real NOT NULL,
    "skewness_volume" real NOT NULL,
    "max_volume" real NOT NULL,
    
    "buy_ratio" real NOT NULL,
    "buy_volume_ratio" real NOT NULL,
    "liquidation_volume_ratio" real NOT NULL,
    "num_trades" real NOT NULL,

    "price_q10" real NOT NULL,
    "price_q25" real NOT NULL,
    "price_q50" real NOT NULL,
    "price_q75" real NOT NULL,
    "price_q90" real NOT NULL,
    
    "close_prev_period0" real NOT NULL,
    "close_prev_period1" real NOT NULL,
    "close_prev_period2" real NOT NULL,

    "price_regression_slope" real NOT NULL,
    "price_regression_intercept" real NOT NULL,
    "price_regression_correlation" real NOT NULL,
    "price_log_regression_slope" real NOT NULL,
    "price_log_regression_intercept" real NOT NULL,
    
    "trade_id" bigint NOT NULL DEFAULT -1,
    
    PRIMARY KEY (time)
);
END;

--ALTER TABLE IF EXISTS ftx.ftx_trade_agg_template
--   OWNER TO cryptodduser;

ALTER TABLE IF EXISTS ftx.ftx_trade_agg_template
    ADD CONSTRAINT ftx_trade_agg_template_time_positive CHECK (time > 0);

COMMIT;