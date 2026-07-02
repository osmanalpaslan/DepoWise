# DepoWise.Api — Fly.io/Docker imajı (.NET 8 ASP.NET). Veri (SQLite + backups + releases) /data kalıcı diskte.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/DepoWise.Api/DepoWise.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV DEPOWISE_SERVER_DATA=/data
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DepoWise.Api.dll"]
