# Local infrastructure

A database and a log sink, so you don't spend your first hour on `docker pull`.

```
docker compose up -d
```

| | Address | Credentials |
|---|---|---|
| SQL Server | `localhost,1433` | `sa` / `Local_Dev_Password_1` |
| Seq | `http://localhost:5341` | none |

Host ports are overridable if something already has them:

```
SQL_PORT=14330 SEQ_PORT=5342 docker compose up -d
```

---

**This is not a starting point for your solution.** There are no application services in here, no project
structure, and no opinion about either. What services you build, how many, and where the boundaries fall is
the part we are interested in — so we have deliberately not answered it.

If your design needs something else — a broker, a cache, blob storage — add it here yourself, choosing
from the allowed images listed in the task statement. Only those two are running by default because they
are the two every solution ends up needing; the rest depend on decisions that are yours, not ours.
