FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY FinGuardPlatform.ServiceDefaults ./FinGuardPlatform.ServiceDefaults
COPY FinGuardPlatform.CoreEngine ./FinGuardPlatform.CoreEngine
COPY FinGuardPlatform.McpServer ./FinGuardPlatform.McpServer
RUN dotnet publish FinGuardPlatform.McpServer/FinGuardPlatform.McpServer.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "FinGuardPlatform.McpServer.dll"]
