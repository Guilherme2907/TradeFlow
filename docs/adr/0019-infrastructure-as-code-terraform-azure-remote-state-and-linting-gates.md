# ADR-0019: Infrastructure as Code with Terraform, Azure Remote State Locking, Modular Topology & CI Linting Gates

- **Status**: Accepted
- **Date**: 2026-11-17
- **Deciders**: Developer (Lead Architect), AI Mentor
- **Relevant Context**: Phase 11 (Infrastructure as Code & Production Azure Cloud Provisioning)

---

## 1. Context & Background

TradeFlow already runs as a containerized .NET 10 Modular Monolith with Kubernetes manifests (`ADR-0017`), OAuth2/OIDC security (`ADR-0013`, `ADR-0018`), PostgreSQL schema isolation (`ADR-0007`), Redis distributed caching/rate-limiting (`ADR-0014`, `ADR-0016`), and OpenTelemetry observability (`ADR-0015`).

To deploy TradeFlow reliably into Microsoft Azure without manual portal clicking ("ClickOps"), configuration drift, or insecure network exposure, we must define our cloud topology declaratively using Infrastructure as Code (IaC).

---

## 2. Problem Statement

We must determine:
1. **State Management & Concurrency**: Where Terraform stores `terraform.tfstate`, why committing state to Git is forbidden, and how concurrent deployments are serialized.
2. **Network & Secret Isolation**: How managed PostgreSQL and Redis instances are isolated from the public internet and how database credentials are generated and injected into AKS workloads without hardcoding secrets in `.tf` files.
3. **Code Organization & Environment Parity**: How Terraform code is structured across reusable modules (`networking`, `aks`, `postgresql`, `redis`, `keyvault`) and isolated environments (`dev`, `prod`).
4. **Zero-Cost CI/CD Quality Gates**: How GitHub Actions validates formatting, syntax, Azure provider rules, and security posture automatically on every PR without spending cloud credits.

---

## 3. Options Considered

### State Management Strategy
- **Option A: Azure Blob Storage Remote Backend with Entra ID Auth & Blob Lease Locking (`azurerm`)**
  - *Pros*: State never touches local disks or Git history; native Azure Blob Lease mechanism guarantees atomic distributed locking during concurrent `plan`/`apply` runs; supports encryption at rest and RBAC access control (`use_azuread_auth = true`).
  - *Cons*: Requires a tiny one-time bootstrap Storage Account and Blob Container prior to initializing environments.
- **Option B: Storing `terraform.tfstate` in the Git Repository**
  - *Pros*: Zero external backend setup.
  - *Cons*: **Catastrophic security & reliability anti-pattern** — `tfstate` contains database passwords, connection strings, and keys in plaintext inside Git history, and Git lacks real-time distributed locking, leading to merge corruption and split-brain cloud drift during concurrent CI runs.

### Code Structure & Environment Isolation
- **Option A: Reusable Child Modules + Isolated Environment Root Folders (`environments/dev`, `environments/prod`)**
  - *Pros*: Prevents blast-radius contamination between `dev` and `prod` (completely separate state files and remote backend keys); guarantees identical topology across environments while scaling SKUs, node counts, and HA settings via variables.
  - *Cons*: Slight boilerplate across environment root folders compared to Terraform Workspaces.
- **Option B: Terraform CLI Workspaces in a Single Root Directory**
  - *Pros*: Less file duplication for trivial setups.
  - *Cons*: High risk of human error applying `prod` changes while pointing to the wrong active workspace; difficult to diverge backend permissions cleanly between `dev` and `prod` subscriptions.

### Secret & Network Architecture
- **Option A: Private VNet Subnets (Delegation / Private Endpoints) + Terraform `random_password` + Azure Key Vault + Workload Identity**
  - *Pros*: PostgreSQL (`public_network_access_enabled = false`) and Redis have zero public IP exposure; AKS pods resolve services via Azure Private DNS Zones; passwords are generated cryptographically at provision time and written directly into Key Vault without human visibility or plaintext `.tf` placeholders.
  - *Cons*: Requires Private DNS Zone links and Workload Identity / Secrets Store CSI configuration on AKS.
- **Option B: Public Firewall IP Allowlists + Static Passwords / Placeholder Substitution in `.tf`**
  - *Pros*: Faster initial setup.
  - *Cons*: Leaks credentials in version control or CI logs and widens network attack surface across shared cloud IP pools.

---

## 4. Decision

We adopt:
1. **Modular Terraform Architecture (`infra/terraform/`)** composed of five single-responsibility modules:
   - `modules/networking`: Virtual Network, isolated subnets (AKS CNI, PostgreSQL Flexible Server delegated subnet, Private Endpoints subnet), and Network Security Groups (NSGs).
   - `modules/keyvault`: Azure Key Vault configured with RBAC authorization, soft-delete retention, purge protection (configurable per environment), and private network ACL controls.
   - `modules/postgresql`: Azure Database for PostgreSQL Flexible Server with VNet Delegation, Private DNS Zone (`privatelink.postgres.database.azure.com`), mandatory SSL, and generated administrator credentials persisted to Key Vault.
   - `modules/redis`: Azure Cache for Redis (`TLS 1.2` minimum, public access disabled) exposed exclusively via a Private Endpoint and Private DNS Zone (`privatelink.redis.cache.windows.net`).
   - `modules/aks`: Azure Kubernetes Service with System + User node pools, Azure CNI networking, Managed Identity (`SystemAssigned`), OIDC Issuer & Workload Identity enabled, and `AcrPull` integration readiness.
2. **Environment-Per-Directory Isolation (`environments/dev` and `environments/prod`)** backed by distinct Azure Blob Storage state keys (`tradeflow-dev.tfstate`, `tradeflow-prod.tfstate`) with `use_azuread_auth = true`.
3. **Strict `.gitignore` Guardrails**: Explicitly ignore `.terraform/`, `*.tfstate`, `*.tfstate.*`, and concrete `*.tfvars` files (while versioning `*.tfvars.example` and `.terraform.lock.hcl` for provider checksum pinning).
4. **Zero-Cost CI/CD Static Verification Pipeline**: A dedicated `terraform-validation` stage in `.github/workflows/ci-cd.yml` enforcing:
   - `terraform fmt -check -recursive`
   - `terraform init -backend=false` + `terraform validate` across all environments and modules
   - `tflint` (with `azurerm` ruleset) for cloud SKU/semantic linting
   - `trivy config` (IaC security scanning) checking misconfigurations without requiring live Azure credentials or cloud spend.

---

## 5. Trade-offs & Consequences

- **Positive Consequences (Gains)**:
  - Zero ClickOps drift and reproducible multi-environment Azure topology.
  - Defense-in-depth data tier: neither PostgreSQL nor Redis is routable from the public internet.
  - Immediate feedback on Terraform syntax, formatting, provider compliance, and security misconfigurations in every Pull Request at \$0 cloud cost.
- **Negative Consequences (Costs / Liabilities)**:
  - Requires bootstrapping the remote state Storage Account (`rg-tradeflow-tfstate`) once before the first live `terraform init` against Azure.
  - Private Endpoints + AKS Azure CNI + Flexible Server VNet Delegation introduce steeper networking troubleshooting complexity compared to public endpoints.

---

## 6. Conditions for Revisiting

- Revisit module boundaries and introduce **Terragrunt** or multi-region hub-and-spoke VNet peering if TradeFlow expands to 3+ regional active-active clusters or requires dozens of independent microservice repositories sharing base VPC/VNet state.
- Revisit static-only CI gates and add automated **Terratest (Go)** ephemeral sandbox suites if an engineering budget for nightly cloud integration environments is approved.
