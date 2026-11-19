data "azurerm_client_config" "current" {}

locals {
  environment = "dev"
  name_prefix = "tradeflow-${local.environment}"
  tags = {
    project     = "tradeflow"
    environment = local.environment
    managed_by  = "terraform"
  }
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${local.name_prefix}"
  location = var.location
  tags     = local.tags
}

module "networking" {
  source = "../../modules/networking"

  name_prefix                     = local.name_prefix
  location                        = azurerm_resource_group.this.location
  resource_group_name             = azurerm_resource_group.this.name
  vnet_address_space              = ["10.10.0.0/16"]
  aks_subnet_prefix               = "10.10.0.0/22"
  postgresql_subnet_prefix        = "10.10.4.0/24"
  private_endpoints_subnet_prefix = "10.10.5.0/24"
  tags                            = local.tags
}

module "keyvault" {
  source = "../../modules/keyvault"

  vault_name                    = "kv-${local.name_prefix}-${var.unique_suffix}"
  name_prefix                   = local.name_prefix
  location                      = azurerm_resource_group.this.location
  resource_group_name           = azurerm_resource_group.this.name
  vnet_id                       = module.networking.vnet_id
  private_endpoints_subnet_id   = module.networking.private_endpoints_subnet_id
  allowed_subnet_ids            = [module.networking.aks_subnet_id]
  allowed_ip_rules              = var.authorized_ip_ranges
  public_network_access_enabled = true
  purge_protection_enabled      = false
  tags                          = local.tags
}

resource "azurerm_role_assignment" "deployer_kv_officer" {
  scope                = module.keyvault.key_vault_id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# In Azure Free Trial, Microsoft restricts external PostgreSQL Flexible Server
# and legacy Azure Cache for Redis in East US 2.
# For this lab/dev environment, PostgreSQL and Redis run with Azure Managed Disks
# as Pods inside the AKS cluster (see deploy/k8s/postgres.yaml & redis.yaml),
# saving cost and avoiding cloud regional quota restrictions.

module "aks" {
  source = "../../modules/aks"

  name_prefix                       = local.name_prefix
  acr_name                          = "acrtradeflow${local.environment}${var.unique_suffix}"
  acr_sku                           = "Standard"
  acr_public_network_access_enabled = true
  location                          = azurerm_resource_group.this.location
  resource_group_name               = azurerm_resource_group.this.name
  aks_subnet_id                     = module.networking.aks_subnet_id
  key_vault_id                      = module.keyvault.key_vault_id
  kubernetes_version                = "1.36"
  system_node_vm_size               = "Standard_D2s_v6"
  system_node_min_count             = 1
  system_node_max_count             = 2
  only_critical_addons_enabled      = false
  enable_workload_node_pool         = false
  workload_node_vm_size             = "Standard_D2s_v6"
  workload_min_count                = 0
  workload_max_count                = 0
  authorized_ip_ranges              = var.authorized_ip_ranges
  log_retention_days                = 30
  tags                              = local.tags
}

resource "azurerm_role_assignment" "deployer_aks_rbac_admin" {
  scope                = module.aks.cluster_id
  role_definition_name = "Azure Kubernetes Service RBAC Cluster Admin"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_key_vault_secret" "postgres_connection_string" {
  name            = "postgres-connection-string"
  value           = "Host=tradeflow-postgres-svc;Port=5432;Database=tradeflow;Username=postgres;Password=postgres"
  key_vault_id    = module.keyvault.key_vault_id
  content_type    = "application/x-connection-string"
  expiration_date = var.secret_expiration_date
  depends_on      = [azurerm_role_assignment.deployer_kv_officer]
}

resource "azurerm_key_vault_secret" "redis_connection_string" {
  name            = "redis-connection-string"
  value           = "tradeflow-redis-svc:6379"
  key_vault_id    = module.keyvault.key_vault_id
  content_type    = "application/x-connection-string"
  expiration_date = var.secret_expiration_date
  depends_on      = [azurerm_role_assignment.deployer_kv_officer]
}
