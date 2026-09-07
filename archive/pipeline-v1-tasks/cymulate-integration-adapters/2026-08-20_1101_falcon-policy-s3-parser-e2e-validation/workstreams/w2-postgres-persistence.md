# W2 — isolated PostgreSQL persistence

## Result

**Physical persistence: PASS. Downstream rule semantics: FAIL confirmed.**

The exact 19-row policy frame produced by W1 was passed through the production
`Parser._prepare_policies_df` and `Parser._policies_df_for_write` methods, then
written by the production `SparkDAL.write_to_postgres` JDBC path.

The destination was a task-created PostgreSQL 15.18 container bound only to
`127.0.0.1:55432`, database `cymulate`. No shared or production database was
contacted. The current parser DDL created `integration.parser_output_policies`.

Runtime: Python 3.10.18, OpenJDK 11, Spark 3.3.0. The pinned official PostgreSQL
JDBC 42.7.4 jar had SHA-256
`188976721ead8e8627eb6d8389d500dccc0c9bebd885268a3047180274a6031e`.

## Persisted result

- projected rows: 19
- production-prepared rows: 19
- JDBC-persisted rows read back: 19
- distinct external policy IDs: 19
- required ID/scope null rows: 0
- rows outside the generated batch UUID: 0
- asset-policy edge objects: 47
- table columns: all 26 expected columns, including UUID scope fields,
  `TIMESTAMPTZ`, booleans, integer precedence, and four JSONB columns
- JSONB container types: `settings=array`, `rules=array`,
  `policy_edges=array`, `additional_fields=object` for all 19 rows

`settings` being an array is intentional in the CrowdStrike projection: it is
the normalized `[{id,value}]` view and is valid JSONB. The database accepted the
producer shape without coercion or loss.

## Downstream-style JSON proof

PostgreSQL `jsonb_to_recordset`, matching the Exposure Analytics consumer seam,
expanded all 939 rules. Every `value` was JSONB type `string`, and all 939 strings
contained a second JSON-encoded object.

Native-field extraction results were:

| rule type | rows | native `enabled` visible | native `detection` visible |
|---|---:|---:|---:|
| `toggle` | 822 | 0 | 0 |
| `slider` | 8 | 0 | 0 |
| `ml_slider_pair` | 109 | 0 | 0 |

This independently confirms W1/W3: the database can store and return the rows,
but the current consumer cannot see the intended nested rule fields because the
producer double-encodes `rules[].value`.

## Runtime notes

The first attempt used Spark's Maven resolver and failed before application
startup because the image's Java trust store rejected Maven TLS. Supplying the
pinned official JDBC jar locally fixed only that runtime issue. No product source
was modified.
