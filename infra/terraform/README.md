# TradeFlow Infrastructure as Code (Terraform on Microsoft Azure)

This directory contains the declarative Azure cloud topology for **TradeFlow**, implemented according to [`ADR-0019`](../../docs/adr/0019-infrastructure-as-code-terraform-azure-remote-state-and-linting-gates.md).

---

## 1. Network & Security Topology

```mermaid
flowchart TB
    Internet["Public Internet"] -. "Blocked (public_network_access_enabled = false)" .-> DataTier
    Admin["VPN / Static CI Egress CIDR"] -->|"authorized_ip_ranges"| AKS_API["AKS Control Plane"]

    subgraph VNet["Virtual Network (10.10.0.0/16 dev | 10.20.0.0/16 prod)"]
        subgraph AKS_Subnet["snet-aks (/22) + NSG"]
            Pods["TradeFlow Pods (Workload Identity OIDC)"]
        end

        subgraph PG_Subnet["snet-postgres (/24) Delegated"]
            Postgres["PostgreSQL 16 Flexible Server (SSL Required)"]
        end

        subgraph PE_Subnet["snet-pe (/24) + NSG"]
            RedisPE["Private Endpoint: Redis (TLS 1.2)"]
            KVPE["Private Endpoint: Azure Key Vault (RBAC)"]
        end
    end

    Pods -->|"Private DNS (privatelink.postgres...)"| Postgres
    Pods -->|"Private DNS (privatelink.redis...)"| RedisPE
    Pods -->|"Azure AD Token Exchange + CSI Driver"| KVPE
```

### Core Security Guarantees
1. **Remote State & Locking (`backend.tf`)**: State is stored in an Azure Storage Account (`sttradeflowtfstate`) using Entra ID authentication (`use_azuread_auth = true`) and native **Azure Blob Lease Locking**. Never store `.tfstate` in Git.
2. **Zero Public Database Exposure**: PostgreSQL (`delegated_subnet_id`) and Redis (`Private Endpoint`) disable public network access (`public_network_access_enabled = false`).
3. **Zero Plaintext Secrets in `.tf`**: `random_password` generates cryptographically secure database credentials directly inside Terraform state and persists connection strings into Azure Key Vault (`azurerm_key_vault_secret`).
4. **Workload Identity (Secretless Pod Auth)**: AKS enables OIDC Issuer federation so pods authenticate to Azure Key Vault via Kubernetes `ServiceAccount` (`tradeflow-api-sa`) without embedding static secrets in Kubernetes manifests.

---

## 2. One-Time Remote State Bootstrap (Per Subscription)

Before executing `terraform init` against Azure for the first time, provision the remote state backend Storage Account:

```bash
az group create --name rg-tradeflow-tfstate --location eastus2
az storage account create \
  --name sttradeflowtfstate \
  --resource-group rg-tradeflow-tfstate \
  --location eastus2 \
  --sku Standard_LRS \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false
az storage container create --name tfstate --account-name sttradeflowtfstate --auth-mode login
```

---

## 3. Environment Execution Workflow

> **Important Networking Note**: Because `azurerm_key_vault` sets `public_network_access_enabled = false`, `terraform apply` (which writes `azurerm_key_vault_secret` over the Key Vault data plane) must execute from a self-hosted CI runner inside the VNet or a workstation connected over Private Link / VPN.

```bash
cd infra/terraform/environments/dev
cp terraform.tfvars.example terraform.tfvars
terraform init
terraform plan -out=tfplan
terraform apply tfplan
```

---

## 4. Zero-Cost CI/CD Static Validation Gates

Every Pull Request validates Terraform code in GitHub Actions (`.github/workflows/ci-cd.yml`) at **\$0 cloud cost**:
1. `terraform fmt -check -recursive`
2. `terraform init -backend=false` + `terraform validate` (for both `dev` and `prod`)
3. `tflint --recursive` (with `tflint-ruleset-azurerm`)
4. `trivy config` (Static IaC security & compliance audit)
