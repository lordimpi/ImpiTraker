FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["ImpiTrack.Api/ImpiTrack.Api.csproj", "ImpiTrack.Api/"]
COPY ["ImpiTrack.Auth.Infrastructure/ImpiTrack.Auth.Infrastructure.csproj", "ImpiTrack.Auth.Infrastructure/"]
COPY ["ImpiTrack.DataAccess/ImpiTrack.DataAccess.csproj", "ImpiTrack.DataAccess/"]
COPY ["ImpiTrack.Application/ImpiTrack.Application.csproj", "ImpiTrack.Application/"]
COPY ["ImpiTrack.Shared/ImpiTrack.Shared.csproj", "ImpiTrack.Shared/"]
COPY ["ImpiTrack.Observability/ImpiTrack.Observability.csproj", "ImpiTrack.Observability/"]
COPY ["ImpiTrack.Ops/ImpiTrack.Ops.csproj", "ImpiTrack.Ops/"]
COPY ["ImpiTrack.Tcp.Core/ImpiTrack.Tcp.Core.csproj", "ImpiTrack.Tcp.Core/"]
COPY ["ImpiTrack.Protocols.Abstractions/ImpiTrack.Protocols.Abstractions.csproj", "ImpiTrack.Protocols.Abstractions/"]
COPY ["ImpiTrack.Protocols.Coban/ImpiTrack.Protocols.Coban.csproj", "ImpiTrack.Protocols.Coban/"]
COPY ["ImpiTrack.Protocols.Cantrack/ImpiTrack.Protocols.Cantrack.csproj", "ImpiTrack.Protocols.Cantrack/"]
COPY ["TcpServer/TcpServer.csproj", "TcpServer/"]

RUN dotnet restore "ImpiTrack.Api/ImpiTrack.Api.csproj"
COPY . .
RUN dotnet publish "ImpiTrack.Api/ImpiTrack.Api.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ImpiTrack.Api.dll"]
