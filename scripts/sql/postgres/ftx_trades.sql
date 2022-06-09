BEGIN;

CREATE TABLE IF NOT EXISTS ftx.ftx_trade_template
(
	id bigserial NOT NULL,
    "time" bigint NOT NULL,
    "price" real NOT NULL,
    "volume" real NOT NULL,
    "flag" smallint NOT NULL,
    PRIMARY KEY (id)
);
END;

ALTER TABLE IF EXISTS ftx.ftx_trade_template
   OWNER TO cryptodduser;

CREATE UNIQUE INDEX ftx_trade_template_time_id_unique
    ON ftx.ftx_trade_template USING btree
        ("time", "id")
;

CREATE INDEX ftx_trade_template_time
    ON ftx.ftx_trade_template USING btree
    ("time")
;

ALTER TABLE IF EXISTS ftx.ftx_trade_template
    ADD CONSTRAINT ftx_trade_template_time_positive CHECK (time > 0);

ALTER TABLE IF EXISTS ftx.ftx_trade_template
    ADD CONSTRAINT ftx_trade_template_price_positive CHECK (price > 0 AND price != double precision 'Nan' AND price < double precision '+infinity');

ALTER TABLE IF EXISTS ftx.ftx_trade_template
    ADD CONSTRAINT ftx_trade_template_volume_positive CHECK (volume >= 0 AND volume != double precision 'Nan' AND volume < double precision '+infinity');

ALTER TABLE IF EXISTS ftx.ftx_trade_template
    ADD CONSTRAINT ftx_trade_template_valid_flag CHECK ("flag" >= 0  AND "flag" < (1 << 3));

COMMIT;