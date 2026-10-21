# ─────────────────────────────────────────────────────────────
# Stage 1 — Build & Publish
# ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore first (Docker layer cache optimisation)
COPY MyOnlineCv/MyOnlineCv.csproj MyOnlineCv/
RUN dotnet restore MyOnlineCv/MyOnlineCv.csproj

# Copy the rest of the source and publish
COPY MyOnlineCv/ MyOnlineCv/
WORKDIR /src/MyOnlineCv
RUN dotnet publish MyOnlineCv.csproj -c Release -o /app/publish --no-restore

# ─────────────────────────────────────────────────────────────
# Stage 2 — Runtime
# ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Render / hosting platforms inject DATABASE_URL at runtime.
# You can also pass it via:  docker run -e DATABASE_URL="postgres://..." ...
# Leave it unset here so the app falls back to the appsettings connection string.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyOnlineCv.dll"]
