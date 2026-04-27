FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["shared/EntornExamen.Shared.csproj", "shared/"]
COPY ["api/EntornExamen.Api.csproj", "api/"]
RUN dotnet restore "api/EntornExamen.Api.csproj"
COPY shared/ shared/
COPY api/ api/
WORKDIR /src/api
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EntornExamen.Api.dll"]
