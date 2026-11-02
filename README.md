# ParkWise

Car parking management system (.NET 10 + Angular). Work in progress; built phase by phase.

## Run (current state)
```
cp .env.example .env          # set a strong SA password
docker compose up --build     # API on http://localhost:5080 (/health, /swagger)
dotnet test                   # run all tests
```

Full README (architecture diagram, design decisions, screenshots) arrives in the final phase.
