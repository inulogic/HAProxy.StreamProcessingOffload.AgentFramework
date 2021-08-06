FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["example/agent/Agent.csproj", "example/agent/"]
COPY ["HAProxy.Spoe.AgentFramework/HAProxy.Spoe.AgentFramework.csproj", "HAProxy.Spoe.AgentFramework/"]
RUN dotnet restore "examples/agent/Agent.csproj"
COPY . .
WORKDIR "/src/example/agent"
RUN dotnet build "Agent.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Agent.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Agent.dll"]