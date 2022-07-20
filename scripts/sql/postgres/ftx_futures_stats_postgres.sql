CREATE TABLE IF NOT EXISTS ftx_futures_stats
(
	id bigserial NOT NULL,
	market_hash bigint NOT NULL,
    "time" bigint NOT NULL,
    "open_interest" double precision NOT NULL,
    "open_interest_usd" double precision NOT NULL,
    "next_funding_rate" real NOT NULL,
    "spread" real NOT NULL,
    "mark" real NOT NULL,
    PRIMARY KEY (id)
);

--ALTER TABLE IF EXISTS ftx_futures_stats
--   OWNER TO cryptodduser;

CREATE UNIQUE INDEX ftx_futures_stats_time_market_unique
    ON ftx_futures_stats USING btree
    ("time" DESC NULLS LAST, market_hash ASC NULLS LAST)
;

ALTER TABLE IF EXISTS ftx_futures_stats
    ADD CONSTRAINT ftx_futures_stats_market_positive CHECK (market_hash > 0);

ALTER TABLE IF EXISTS ftx_futures_stats
    ADD CONSTRAINT ftx_futures_stats_time_positive CHECK (time > 0);

ALTER TABLE IF EXISTS ftx_futures_stats
    ADD CONSTRAINT ftx_futures_stats_open_interest_positive CHECK (open_interest >= 0 AND open_interest != double precision 'Nan' AND open_interest < double precision '+infinity');

ALTER TABLE IF EXISTS ftx_futures_stats
    ADD CONSTRAINT ftx_futures_stats_open_interest_usd_positive CHECK (open_interest_usd >= 0 AND open_interest_usd != double precision 'Nan' AND open_interest_usd < double precision '+infinity');

ALTER TABLE IF EXISTS ftx_futures_stats
    ADD CONSTRAINT ftx_futures_stats_next_funding_rate_finite CHECK (next_funding_rate != real 'Nan' AND next_funding_rate < real '+infinity' AND next_funding_rate > real '-infinity');

ALTER TABLE IF EXISTS ftx_futures_stats
    ADD CONSTRAINT ftx_futures_stats_spread_finite CHECK (spread != real 'Nan' AND spread < real '+infinity' AND spread > real '-infinity');

ALTER TABLE IF EXISTS ftx_futures_stats
    ADD CONSTRAINT ftx_futures_stats_mark_positive CHECK (mark >= 0 AND mark != double precision 'Nan' AND mark < double precision '+infinity');