# TradeFlow Kubernetes Deployment Guide (Local & Azure AKS)

This directory contains declarative Kubernetes manifests to run the complete TradeFlow platform (**API**, **Frontend SPA**, **Keycloak**, **RabbitMQ**, and **Apache Kafka KRaft**) inside Kubernetes pods.

---

## Manifest Inventory

| Manifest | Purpose |
| :--- | :--- |
| [`rabbitmq.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/rabbitmq.yaml) | RabbitMQ 4.0 `StatefulSet` (2Gi PVC), Management UI + AMQP `ClusterIP` Service |
| [`kafka.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/kafka.yaml) | Apache Kafka 3.8 KRaft `StatefulSet` (5Gi PVC), headless + `ClusterIP` Services |
| [`keycloak.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/keycloak.yaml) | Keycloak 26 `Deployment` with baked white-label theme + realm (`docker/keycloak/Dockerfile`) |
| [`tradeflow-web.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/tradeflow-web.yaml) | React 19 + Nginx unprivileged SPA `Deployment` (`src/TradeFlow.Web/Dockerfile`) |
| [`tradeflow-local.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/tradeflow-local.yaml) | TradeFlow .NET 10 API for Docker Desktop K8s (connects to Postgres/Redis/Jaeger on host and RabbitMQ/Kafka/Keycloak pods) |
| [`tradeflow-k8s.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/tradeflow-k8s.yaml) | Production AKS manifest (`ServiceAccount` for Azure Workload Identity, HPA, zero-downtime `RollingUpdate`) |

---

## Running Locally on Docker Desktop Kubernetes

1. **Start stateful base services (PostgreSQL, Redis, Jaeger)** on Docker Desktop:
   ```bash
   docker compose up -d postgres redis jaeger
   ```
2. **Build custom container images** locally:
   ```bash
   docker build -t tradeflow-api:local -f Dockerfile .
   docker build -t tradeflow-keycloak:local -f docker/keycloak/Dockerfile .
   docker build -t tradeflow-web:local -f src/TradeFlow.Web/Dockerfile .
   ```
3. **Deploy pods into Kubernetes**:
   ```bash
   kubectl apply -f deploy/k8s/rabbitmq.yaml
   kubectl apply -f deploy/k8s/kafka.yaml
   kubectl apply -f deploy/k8s/keycloak.yaml
   kubectl apply -f deploy/k8s/tradeflow-local.yaml
   kubectl apply -f deploy/k8s/tradeflow-web.yaml
   ```
4. **Expose browser-facing services via port-forward** (if not using LoadBalancer/Ingress):
   ```bash
   kubectl port-forward svc/tradeflow-keycloak-svc 8080:8080
   kubectl port-forward svc/tradeflow-web-svc 5173:5173
   kubectl port-forward svc/tradeflow-rabbitmq-svc 15672:15672
   ```
   Then open `http://localhost:5173` (Frontend), `http://localhost:5022/scalar/v1` (API via LoadBalancer), and `http://localhost:15672` (RabbitMQ UI).

---

## Running on Azure AKS (Provisioned via Terraform)

1. Tag and push `tradeflow-api`, `tradeflow-keycloak`, and `tradeflow-web` to the Azure Container Registry output by Terraform (`acr_login_server`).
2. Annotate `tradeflow-api-sa` in `tradeflow-k8s.yaml` with `workload_identity_client_id`, configure `ConnectionStrings__Database` / `ConnectionStrings__Redis` (mounted from Azure Key Vault via Secrets Store CSI Driver), and switch `keycloak.yaml` to `args: ["start", "--import-realm"]` backed by Azure PostgreSQL.
3. Apply manifests via `kubectl apply -f deploy/k8s/`.
