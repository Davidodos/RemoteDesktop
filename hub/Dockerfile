# syntax=docker/dockerfile:1

# ---- PWA bauen ------------------------------------------------------------
FROM node:22-alpine AS app-build

WORKDIR /build/app

COPY app/package*.json ./
RUN npm ci

COPY app/ ./
RUN npm run build

# ---- Hub bauen ------------------------------------------------------------
FROM node:22-alpine AS hub-build

WORKDIR /build/hub

COPY hub/package*.json ./
RUN npm ci

COPY hub/tsconfig.json ./
COPY hub/src ./src
RUN npm run build

# ---- Laufzeit -------------------------------------------------------------
FROM node:22-alpine AS runtime

WORKDIR /app

ENV NODE_ENV=production

COPY hub/package*.json ./
RUN npm ci --omit=dev && npm cache clean --force

COPY --from=hub-build /build/hub/dist ./dist
COPY --from=app-build /build/app/dist ./public

# devices.json wird zur Laufzeit gemountet, nicht ins Image gebacken —
# darin stehen die Agent-Tokens.
ENV DEVICES_PATH=/config/devices.json
ENV STATIC_PATH=/app/public
ENV HUB_PORT=3080

EXPOSE 3080

# Kein HEALTHCHECK auf /health mit wget: das Alpine-Image bringt kein curl mit,
# und ein zusätzliches Paket lohnt für den einen Endpoint nicht.
CMD ["node", "dist/index.js"]
