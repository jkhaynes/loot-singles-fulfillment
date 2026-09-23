# Loot Singles Fulfillment — one image, one origin.
#
# The API and the built web application are served together from a single origin (PRD §40.8, amended
# 2026-09-22 by A17). The session cookie is SameSite=Strict and a browser will not send it across
# origins, so splitting the web app onto its own host breaks authentication outright.
#
# The same image runs three ways, selected by argument:
#   (none)            serve HTTP on 8080
#   migrate           apply pending migrations and exit
#   bootstrap-admin   create the first manager account and exit
# That is why there is an ENTRYPOINT and no CMD.

# ---------------------------------------------------------------------------
# Stage 1 — build the web application
# ---------------------------------------------------------------------------
FROM node:24-alpine AS web

WORKDIR /src/frontend

# Copy the manifests first so `npm ci` is cached until dependencies actually change.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

# ---------------------------------------------------------------------------
# Stage 2 — publish the API
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api

WORKDIR /src

# Project files first, for the same caching reason. Publishing the API project pulls in exactly the
# three libraries it references — deliberately not the solution, which also carries the E2E host and
# its Testcontainers dependency, neither of which belongs in a production image.
COPY backend/src/LootSingles.Api/LootSingles.Api.csproj backend/src/LootSingles.Api/
COPY backend/src/LootSingles.Application/LootSingles.Application.csproj backend/src/LootSingles.Application/
COPY backend/src/LootSingles.Domain/LootSingles.Domain.csproj backend/src/LootSingles.Domain/
COPY backend/src/LootSingles.Infrastructure/LootSingles.Infrastructure.csproj backend/src/LootSingles.Infrastructure/
RUN dotnet restore backend/src/LootSingles.Api/LootSingles.Api.csproj

COPY backend/src/ backend/src/
RUN dotnet publish backend/src/LootSingles.Api/LootSingles.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ---------------------------------------------------------------------------
# Stage 3 — runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=api /app/publish ./
# The web build lands in wwwroot, where UseStaticFiles and MapFallbackToFile serve it.
COPY --from=web /src/frontend/dist ./wwwroot/

# Container Apps routes to this port; its ingress terminates TLS and forwards plain HTTP, which is
# why Program.cs honours X-Forwarded-Proto.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Run as the non-root user the base image provides. A container that serves customer data has no
# reason to be root, and the registry holding this image is public.
USER $APP_UID

ENTRYPOINT ["dotnet", "LootSingles.Api.dll"]
