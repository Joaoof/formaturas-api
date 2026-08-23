FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/FormaturasFlow.Api/FormaturasFlow.Api.csproj src/FormaturasFlow.Api/
RUN dotnet restore src/FormaturasFlow.Api/FormaturasFlow.Api.csproj

COPY src/ src/
RUN dotnet publish src/FormaturasFlow.Api/FormaturasFlow.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd -r app && useradd -r -g app -u 1000 app

COPY --from=build --chown=app:app /app/publish .

USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FormaturasFlow.Api.dll"]
