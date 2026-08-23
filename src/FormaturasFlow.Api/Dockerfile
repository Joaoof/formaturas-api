# Dockerfile multi-stage para .NET 10.
# Doc oficial: https://learn.microsoft.com/dotnet/architecture/containerized-lifecycle/design-docker-image
# Imagens oficiais MS: https://mcr.microsoft.com/artifact/mar/dotnet/aspnet

# ---------- STAGE 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia csproj primeiro para aproveitar cache de camada em rebuilds.
COPY src/FormaturasFlow.Api/FormaturasFlow.Api.csproj ./FormaturasFlow.Api/
RUN dotnet restore ./FormaturasFlow.Api/FormaturasFlow.Api.csproj

# Copia o restante e publica em Release AOT-friendly.
COPY src/FormaturasFlow.Api/ ./FormaturasFlow.Api/
RUN dotnet publish ./FormaturasFlow.Api/FormaturasFlow.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---------- STAGE 2: runtime (imagem enxuta) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Usuário não-root — hardening padrão de container.
ARG UID=1001
RUN adduser --disabled-password --gecos "" --uid $UID appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

# Healthcheck bate no endpoint /health que já existe.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget --spider -q http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FormaturasFlow.Api.dll"]
