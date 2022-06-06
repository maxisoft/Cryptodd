CREATE OR REPLACE FUNCTION current_time_millisecond() RETURNS BIGINT
LANGUAGE SQL STABLE AS $$
    select (extract(epoch from now()) * 1000)::BIGINT
$$;