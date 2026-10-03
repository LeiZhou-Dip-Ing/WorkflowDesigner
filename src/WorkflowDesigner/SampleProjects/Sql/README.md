# Process data SQL example

Copy `TpxAnalogData.sql` into a Project SQL script. In a workflow Method, use **Modify SQL** and select that script. Its four parameters appear as separate inputs; bind `arbpl_id`, `datum`, `wert0`, and `wert1` to the corresponding variables. Add `anada_wert2` through `anada_wert29` only for points in the configured PV mapping.

Put the existing OPC UA handshake/condition Actions before **Modify SQL**. Store the affected row count in a Method variable; acknowledge `neue_Daten_vorhanden` only when the SQL Action succeeds and one row was inserted. A database error leaves the handshake unacknowledged. The table's unique key on `(anada_arbpl_id, anada_datum)` prevents duplicate process records.

Configure the Runtime database in **Settings > Database**, save, and run **Test connection**. The SQL editor's **Validate** checks syntax, while **Execute** asks for sample parameter values and confirmation before running against that configured database. No connection string or credentials are included in this example.
