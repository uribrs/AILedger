import json
import os
import uuid

import psycopg2
from loguru import logger
from pyspark.sql import SparkSession

from dal.dbManager import TABLES
from dal.sparkDAL import SparkDAL
from parsers import parser_output_policies_create_table_schema


def main():
    parquet_path = os.environ["POLICIES_PARQUET"]
    jdbc_jar = os.environ["POSTGRES_JDBC_JAR"]
    host = os.environ.get("POSTGRES_HOST", "host.docker.internal")
    port = os.environ.get("POSTGRES_PORT", "55432")
    database = os.environ.get("POSTGRES_DB", "cymulate")
    batch_id = str(uuid.uuid4())
    env_vars = {
        "CLIENT_ID": "e2e-local-client",
        "CLIENT_INTEGRATION_ID": str(uuid.uuid4()),
        "INSTANCE_ID": str(uuid.uuid4()),
        "INTEGRATION_SETTING_ID": str(uuid.uuid4()),
        "INTEGRATION_SETTING_FLOW_ID": str(uuid.uuid4()),
        "FLOW_NAME": "Crowdstrike Falcon - Spotlight Vulnerability Management",
        "TENANT_ID": "default",
        "postgresHostname": host,
        "postgresPort": port,
        "postgresUsername": "postgres",
        "postgresPassword": "postgres",
    }

    spark = (
        SparkSession.builder.master("local[4]")
        .appName("falcon-policy-e2e-persistence")
        .config("spark.driver.memory", "6g")
        .config("spark.jars", jdbc_jar)
        .config("spark.sql.session.timeZone", "UTC")
        .getOrCreate()
    )
    spark.sparkContext.setLogLevel("WARN")
    conn = psycopg2.connect(
        database=database,
        user="postgres",
        password="postgres",
        host=host,
        port=int(port),
        sslmode="disable",
    )
    try:
        with conn.cursor() as cursor:
            cursor.execute("CREATE SCHEMA IF NOT EXISTS integration")
            cursor.execute(parser_output_policies_create_table_schema)
            conn.commit()

        from importlib.util import module_from_spec, spec_from_file_location

        spec = spec_from_file_location("cybi_parser_script_e2e", "/app/jobs/cybi-parser/script.py")
        module = module_from_spec(spec)
        spec.loader.exec_module(module)
        job_parser = module.Parser.__new__(module.Parser)
        job_parser.env_vars = env_vars
        job_parser.batch_id = batch_id

        projected = spark.read.parquet(parquet_path)
        projected_count = projected.count()
        prepared = job_parser._prepare_policies_df(projected).cache()
        prepared_count = prepared.count()
        write_df = job_parser._policies_df_for_write(prepared)

        dal = SparkDAL(
            connection=conn,
            logger=logger,
            env_vars=env_vars,
            tenant_id_to_database={},
        )
        dal.delete_query(
            "DELETE FROM integration.parser_output_policies "
            f"WHERE client_id = 'e2e-local-client' AND batch_id = '{batch_id}'"
        )
        dal.write_to_postgres(
            {"batchsize": "50000", "write_mode": "append"},
            write_df,
            TABLES.PARSER_OUTPUT_POLICIES,
        )

        with conn.cursor() as cursor:
            cursor.execute(
                """
                SELECT
                    COUNT(*),
                    COUNT(DISTINCT external_id),
                    COUNT(*) FILTER (WHERE id IS NULL OR client_id IS NULL OR instance_id IS NULL),
                    COUNT(*) FILTER (WHERE batch_id IS DISTINCT FROM %s::uuid),
                    COUNT(*) FILTER (WHERE jsonb_typeof(settings) <> 'object'),
                    COUNT(*) FILTER (WHERE jsonb_typeof(rules) <> 'array'),
                    COUNT(*) FILTER (WHERE jsonb_typeof(policy_edges) <> 'array'),
                    COALESCE(SUM(jsonb_array_length(policy_edges)), 0)
                FROM integration.parser_output_policies
                WHERE client_id = 'e2e-local-client' AND batch_id = %s::uuid
                """,
                (batch_id, batch_id),
            )
            row = cursor.fetchone()
            cursor.execute(
                """
                SELECT COALESCE(jsonb_typeof(rule -> 'value'), 'sql-null'), COUNT(*)
                FROM integration.parser_output_policies p
                CROSS JOIN LATERAL jsonb_array_elements(p.rules) AS rule
                WHERE p.client_id = 'e2e-local-client' AND p.batch_id = %s::uuid
                GROUP BY 1 ORDER BY 1
                """,
                (batch_id,),
            )
            rule_value_json_types = dict(cursor.fetchall())
            cursor.execute(
                """
                SELECT COUNT(*)
                FROM integration.parser_output_policies p
                CROSS JOIN LATERAL jsonb_array_elements(p.rules) AS rule
                WHERE p.client_id = 'e2e-local-client' AND p.batch_id = %s::uuid
                  AND jsonb_typeof(rule -> 'value') = 'string'
                  AND left(rule ->> 'value', 1) IN ('{', '[')
                """,
                (batch_id,),
            )
            stringified_structured_rule_values = cursor.fetchone()[0]
            cursor.execute(
                """
                SELECT column_name, data_type, udt_name, is_nullable
                FROM information_schema.columns
                WHERE table_schema = 'integration' AND table_name = 'parser_output_policies'
                ORDER BY ordinal_position
                """
            )
            columns = [dict(zip(("name", "data_type", "udt_name", "nullable"), value)) for value in cursor.fetchall()]

        print(json.dumps({
            "projected_rows": projected_count,
            "prepared_rows": prepared_count,
            "persisted_rows": row[0],
            "distinct_external_ids": row[1],
            "required_scope_null_rows": row[2],
            "wrong_batch_rows": row[3],
            "invalid_settings_json_rows": row[4],
            "invalid_rules_json_rows": row[5],
            "invalid_policy_edges_json_rows": row[6],
            "asset_policy_edges": row[7],
            "rule_value_json_types": rule_value_json_types,
            "stringified_structured_rule_values": stringified_structured_rule_values,
            "table_columns": columns,
        }, sort_keys=True))
        prepared.unpersist()
    finally:
        conn.close()
        spark.stop()


if __name__ == "__main__":
    main()
