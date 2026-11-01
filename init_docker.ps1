$dockerfile = @"
FROM ://microsoft.com AS build
WORKDIR /src
COPY ["FinGuardPlatform.AppHost/FinGuardPlatform.AppHost.csproj", "FinGuardPlatform.AppHost/"]
COPY ["FinGuardPlatform.ServiceDefaults/FinGuardPlatform.ServiceDefaults.csproj", "FinGuardPlatform.ServiceDefaults/"]
RUN dotnet restore "FinGuardPlatform.AppHost/FinGuardPlatform.AppHost.csproj"
COPY . .
WORKDIR "/src/FinGuardPlatform.AppHost"
RUN dotnet publish "FinGuardPlatform.AppHost.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM ://microsoft.com AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 17000
EXPOSE 17100
ENTRYPOINT ["dotnet", "FinGuardPlatform.AppHost.dll"]
"@

$dockerfile | docker build -t finguard-apphost:latest -f - .
