FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ParkWise.Domain/*.csproj src/ParkWise.Domain/
COPY src/ParkWise.Application/*.csproj src/ParkWise.Application/
COPY src/ParkWise.Infrastructure/*.csproj src/ParkWise.Infrastructure/
COPY src/ParkWise.Api/*.csproj src/ParkWise.Api/
RUN dotnet restore src/ParkWise.Api/ParkWise.Api.csproj
COPY src/ src/
RUN dotnet publish src/ParkWise.Api/ParkWise.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ParkWise.Api.dll"]
