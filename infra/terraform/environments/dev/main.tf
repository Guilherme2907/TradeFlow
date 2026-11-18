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

  vault_name                  = "kv-${local.name_prefix}-${var.unique_suffix}"
  name_prefix                 = local.name_prefix
  location                    = azurerm_resource_group.this.location
  resource_group_name         = azurerm_resource_group.this.name
  vnet_id                     = module.networking.vnet_id
  private_endpoints_subnet_id = module.networking.private_endpoints_subnet_id
  allowed_subnet_ids          = [module.networking.aks_subnet_id]
  purge_protection_enabled    = true
  tags                        = local.tags
}

module "postgresql" {
  source = "../../modules/postgresql"

  name_prefix                  = local.name_prefix
  location                     = azurerm_resource_group.this.location
  resource_group_name          = azurerm_resource_group.this.name
  vnet_id                      = module.networking.vnet_id
  delegated_subnet_id          = module.networking.postgresql_subnet_id
  sku_name                     = "B_Standard_B2s"
  storage_mb                   = 32768
  backup_retention_days        = 7
  geo_redundant_backup_enabled = false
  high_availability_enabled    = false
  tags                         = local.tags
}

module "redis" {
  source = "../../modules/redis"

  name_prefix                 = local.name_prefix
  location                    = azurerm_resource_group.this.location
  resource_group_name         = azurerm_resource_group.this.name
  vnet_id                     = module.networking.vnet_id
  private_endpoints_subnet_id = module.networking.private_endpoints_subnet_id
  sku_name                    = "Standard"
  capacity                    = 1
  tags                        = local.tags
}

module "aks" {
  source = "../../modules/aks"

  name_prefix                       = local.name_prefix
  acr_name                          = "acrtradeflow${local.environment}${var.unique_suffix}"
  acr_sku                           = "Standard"
  acr_public_network_access_enabled = false
  location                          = azurerm_resource_group.this.location
  resource_group_name               = azurerm_resource_group.this.name
  aks_subnet_id                     = module.networking.aks_subnet_id
  key_vault_id                      = module.keyvault.key_vault_id
  system_node_vm_size               = "Standard_D2s_v5"
  system_node_min_count             = 1
  system_node_max_count             = 2
  workload_node_vm_size             = "Standard_D2s_v5"
  workload_min_count                = 1
  workload_max_count                = 3
  authorized_ip_ranges              = var.authorized_ip_ranges
  log_retention_days                = 30
  tags                              = local.tags
}

resource "azurerm_key_vault_secret" "postgres_connection_string" {
  name            = "postgres-connection-string"
  value           = "Host=${module.postgresql.fqdn};Database=${module.postgresql.database_name};Username=${module.postgresql.administrator_login};Password=${module.postgresql.administrator_password};SslMode=Require"
  key_vault_id    = module.keyvault.key_vault_id
  content_type    = "application/x-connection-string"
  expiration_date = var.secret_expiration_date
}

resource "azurerm_key_vault_secret" "redis_connection_string" {
  name            = "redis-connection-string"
  value           = module.redis.primary_connection_string
  key_vault_id    = module.keyvault.key_vault_id
  content_type    = "application/x-connection-string"
  expiration_date = var.secret_expiration_date
}
