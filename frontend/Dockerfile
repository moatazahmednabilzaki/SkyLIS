# Sky LIS portal (client-portal or admin-portal via build arg) — build from the repo root:
#   docker build -f frontend/Dockerfile --build-arg PORTAL=client-portal -t skylis/client-portal .
FROM node:22-alpine AS build
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
ARG PORTAL=client-portal
RUN npx ng build ${PORTAL} --configuration production

FROM nginx:1.27-alpine
ARG PORTAL=client-portal
COPY deploy/nginx/portal.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/${PORTAL}/browser /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
    CMD wget -qO /dev/null http://localhost/ || exit 1
