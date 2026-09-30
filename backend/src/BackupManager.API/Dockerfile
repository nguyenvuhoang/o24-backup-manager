FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet restore backend/src/BackupManager.API/BackupManager.API.csproj
COPY . .
RUN dotnet publish backend/src/BackupManager.API/BackupManager.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends openssh-client curl ca-certificates gnupg \
    && curl -sSL -O https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb \
    && apt-get update && ACCEPT_EULA=Y apt-get install -y --no-install-recommends mssql-tools18 \
    && ln -s /opt/mssql-tools18/bin/sqlcmd /usr/local/bin/sqlcmd \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:5088
EXPOSE 5088
VOLUME ["/app/data", "/backups"]
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD curl -fsS http://127.0.0.1:5088/api/v1/health || exit 1
ENTRYPOINT ["dotnet", "BackupManager.API.dll"]
