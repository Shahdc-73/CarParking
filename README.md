# ParkWise

Car parking management system (.NET 10 + Angular). Work in progress; built phase by phase.

## Run (current state)
```
cp .env.example .env          # set SA password, JWT_KEY (32+ chars), SEED_PASSWORD
docker compose up --build     # API on http://localhost:5080 (/swagger, /health, /health/ready)
dotnet test                   # needs Docker: integration tests start a SQL Server container
```

Demo users (password = your `SEED_PASSWORD`): `admin@`, `attendant@`, `driver1@`, `driver2@` + `parkwise.local`.

Running the API outside Docker:
```
dotnet user-secrets set "Jwt:Key" "<32+ random chars>" --project src/ParkWise.Api
dotnet user-secrets set "Seed:Password" "<demo password>" --project src/ParkWise.Api
```

Full README (architecture diagram, design decisions, screenshots) arrives in the final phase.
