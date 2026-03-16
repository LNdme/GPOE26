#!/bin/bash

export ASPNETCORE_ENVIRONMENT=Development

JWT_KEY="votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits"
JWT_ISSUER="GPOE2026"
JWT_AUDIENCE="GPOE2026Users"

# Build Npgsql-compatible connection string from Replit PG env vars
NPGSQL_CONN="Host=${PGHOST};Port=${PGPORT:-5432};Database=${PGDATABASE};Username=${PGUSER};Password=${PGPASSWORD};SSL Mode=Disable"

echo "Using connection: Host=${PGHOST};Port=${PGPORT:-5432};Database=${PGDATABASE};Username=${PGUSER}"

# Start ApiService (port 5455)
echo "Starting ApiService on port 5455..."
ASPNETCORE_URLS="http://localhost:5455" \
  ConnectionStrings__LyceeDB="$NPGSQL_CONN" \
  dotnet run --project GPOE26.ApiService/GPOE26.ApiService.csproj --no-launch-profile &
APIPID=$!

# Start User service (port 5197)
echo "Starting User service on port 5197..."
ASPNETCORE_URLS="http://localhost:5197" \
  ConnectionStrings__UserDB="$NPGSQL_CONN" \
  Jwt__Key="$JWT_KEY" \
  Jwt__Issuer="$JWT_ISSUER" \
  Jwt__Audience="$JWT_AUDIENCE" \
  dotnet run --project User/User.csproj --no-launch-profile &
USERPID=$!

# Start Cours service (port 5188)
echo "Starting Cours service on port 5188..."
ASPNETCORE_URLS="http://localhost:5188" \
  ConnectionStrings__CoursDB="$NPGSQL_CONN" \
  Jwt__Key="$JWT_KEY" \
  Jwt__Issuer="$JWT_ISSUER" \
  Jwt__Audience="$JWT_AUDIENCE" \
  dotnet run --project Cours/Cours.csproj --no-launch-profile &
COURSPID=$!

# Start Chat service (port 5212)
echo "Starting Chat service on port 5212..."
ASPNETCORE_URLS="http://localhost:5212" \
  dotnet run --project Chat/Chat.csproj --no-launch-profile &
CHATPID=$!

# Start Quiz service (port 5163)
echo "Starting Quiz service on port 5163..."
ASPNETCORE_URLS="http://localhost:5163" \
  dotnet run --project Quiz/Quiz.csproj --no-launch-profile &
QUIZPID=$!

# Wait for backend services to start up
echo "Waiting for backend services to start..."
sleep 30

# Start Web frontend (port 5000)
echo "Starting Web frontend on port 5000..."
ASPNETCORE_URLS="http://0.0.0.0:5000" \
  Services__ApiService="http://localhost:5455" \
  Services__User="http://localhost:5197" \
  Services__Cours="http://localhost:5188" \
  Services__Chat="http://localhost:5212" \
  Services__Quiz="http://localhost:5163" \
  dotnet run --project GPOE26.Web/GPOE26.Web.csproj --no-launch-profile

# If frontend exits, kill all background processes
kill $APIPID $USERPID $COURSPID $CHATPID $QUIZPID 2>/dev/null
wait
