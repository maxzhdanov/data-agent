FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY data ./data
COPY FinGuardPlatform.ServiceDefaults ./FinGuardPlatform.ServiceDefaults
COPY FinGuardPlatform.CoreEngine ./FinGuardPlatform.CoreEngine
COPY FinGuardPlatform.PipelineApp ./FinGuardPlatform.PipelineApp
RUN dotnet publish FinGuardPlatform.PipelineApp/FinGuardPlatform.PipelineApp.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "FinGuardPlatform.PipelineApp.dll"]
