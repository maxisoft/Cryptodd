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
    
    "mean_volume" real NOT NULL,
    "std_volume" real NOT NULL,
    "kurtosis_volume" real NOT NULL,
    "skewness_volume" real NOT NULL,
    
    "buy_ratio" real NOT NULL,
    "liquidation_ratio" real NOT NULL,
    "num_trades" real NOT NULL,

    "price_q10" real NOT NULL,
    "price_q25" real NOT NULL,
    "price_q50" real NOT NULL,
    "price_q75" real NOT NULL,
    "price_q90" real NOT NULL,
    
    PRIMARY KEY (time)
);
END;

ALTER TABLE IF EXISTS ftx.ftx_trade_agg_template
   OWNER TO cryptodduser;

ALTER TABLE IF EXISTS ftx.ftx_trade_agg_template
    ADD CONSTRAINT ftx_trade_agg_template_time_positive CHECK (time > 0);

COMMIT;