FROM node:22-alpine AS build
WORKDIR /app

COPY src/TradeFlow.Web/package.json src/TradeFlow.Web/package-lock.json ./
RUN npm ci

COPY src/TradeFlow.Web/ ./

ARG VITE_API_BASE_URL=http://localhost:5022
ARG VITE_KEYCLOAK_AUTHORITY=http://localhost:8080/realms/tradeflow
ARG VITE_KEYCLOAK_CLIENT_ID=tradeflow-web

ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
ENV VITE_KEYCLOAK_AUTHORITY=$VITE_KEYCLOAK_AUTHORITY
ENV VITE_KEYCLOAK_CLIENT_ID=$VITE_KEYCLOAK_CLIENT_ID

RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY src/TradeFlow.Web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
USER 101
