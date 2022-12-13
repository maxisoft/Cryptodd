-- View: public.table_sizes2

-- DROP VIEW public.table_sizes2;

CREATE OR REPLACE VIEW public.table_sizes2
AS
WITH table_sizes AS (SELECT pg_size_pretty(a.total_bytes) AS total,
                            pg_size_pretty(a.index_bytes) AS index,
                            pg_size_pretty(a.toast_bytes) AS toast,
                            pg_size_pretty(a.table_bytes) AS "table",
                            a.oid,
                            a.table_schema,
                            a.table_name,
                            a.row_estimate,
                            a.total_bytes,
                            a.index_bytes,
                            a.toast_bytes,
                            a.table_bytes
                     FROM (SELECT a_1.oid,
                                  a_1.table_schema,
                                  a_1.table_name,
                                  a_1.row_estimate,
                                  a_1.total_bytes,
                                  a_1.index_bytes,
                                  a_1.toast_bytes,
                                  a_1.total_bytes - a_1.index_bytes -
                                  COALESCE(a_1.toast_bytes, 0::bigint) AS table_bytes
                           FROM (SELECT c.oid,
                                        n.nspname                                         AS table_schema,
                                        c.relname                                         AS table_name,
                                        c.reltuples                                       AS row_estimate,
                                        pg_total_relation_size(c.oid::regclass)           AS total_bytes,
                                        pg_indexes_size(c.oid::regclass)                  AS index_bytes,
                                        pg_total_relation_size(c.reltoastrelid::regclass) AS toast_bytes
                                 FROM pg_class c
                                          LEFT JOIN pg_namespace n ON n.oid = c.relnamespace
                                 WHERE c.relkind = 'r'::"char") a_1) a
                     ORDER BY a.total_bytes DESC)
SELECT x.schema,
       x.name,
       x.total_bytes,
       x.row_estimate,
       x.num_chunks,
       x.compression_enabled,
       x.before_compression_total_bytes,
       x.after_compression_total_bytes
FROM ( SELECT t.hypertable_schema AS schema,
              t.hypertable_name AS name,
              ( SELECT pg_size_pretty(hypertable_detailed_size.total_bytes) AS total_bytes
                FROM hypertable_detailed_size(format('%I.%I'::text, t.hypertable_schema, t.hypertable_name)::regclass) hypertable_detailed_size(table_bytes, index_bytes, toast_bytes, total_bytes, node_name)) AS total_bytes,
              approximate_row_count(format('%I.%I'::text, t.hypertable_schema, t.hypertable_name)::regclass) AS row_estimate,
              t.num_chunks,
              t.compression_enabled,
              ( SELECT pg_size_pretty(hypertable_compression_stats.before_compression_total_bytes) AS before_compression_total_bytes
                FROM hypertable_compression_stats(format('%I.%I'::text, t.hypertable_schema, t.hypertable_name)::regclass) hypertable_compression_stats(total_chunks, number_compressed_chunks, before_compression_table_bytes, before_compression_index_bytes, before_compression_toast_bytes, before_compression_total_bytes, after_compression_table_bytes, after_compression_index_bytes, after_compression_toast_bytes, after_compression_total_bytes, node_name)) AS before_compression_total_bytes,
              ( SELECT pg_size_pretty(hypertable_compression_stats.after_compression_total_bytes) AS after_compression_total_bytes
                FROM hypertable_compression_stats(format('%I.%I'::text, t.hypertable_schema, t.hypertable_name)::regclass) hypertable_compression_stats(total_chunks, number_compressed_chunks, before_compression_table_bytes, before_compression_index_bytes, before_compression_toast_bytes, before_compression_total_bytes, after_compression_table_bytes, after_compression_index_bytes, after_compression_toast_bytes, after_compression_total_bytes, node_name)) AS after_compression_total_bytes
       FROM timescaledb_information.hypertables t
       UNION
       SELECT ts.table_schema AS schema,
              ts.table_name AS name,
              ts.total AS total_bytes,
              ts.row_estimate,
              NULL::bigint AS num_chunks,
              NULL::boolean AS compression_enabled,
              NULL::text AS before_compression_total_bytes,
              NULL::text AS after_compression_total_bytes
       FROM table_sizes ts
       WHERE NOT (format('%I.%I'::text, ts.table_schema, ts.table_name)::regclass::oid IN ( SELECT format('%I.%I'::text, t.hypertable_schema, t.hypertable_name)::regclass AS format
                                                                                            FROM timescaledb_information.hypertables t))) x
WHERE x.schema = 'public' OR x.schema = 'ftx' OR x.schema = 'bitfinex'
ORDER BY (pg_size_bytes(x.total_bytes)) DESC;

--ALTER TABLE public.table_sizes2
--OWNER TO cryptodduser;

--GRANT ALL ON TABLE public.table_sizes2 TO cryptodduser;