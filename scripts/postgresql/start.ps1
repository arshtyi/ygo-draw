$ErrorActionPreference = "Stop"
$pgroot = scoop prefix postgresql
$pgdata = "$pgroot\data"
pg_ctl -D $pgdata -l "$pgdata\server.log" start
