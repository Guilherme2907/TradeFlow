data "azurerm_client_config" "current" {}

resource "azurerm_log_analytics_workspace" "this" {
  name                = "law-${var.name_prefix}"
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = var.tags
}

resource "azurerm_container_registry" "this" {
  name                          = var.acr_name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  sku                           = var.acr_sku
  admin_enabled                 = false
  public_network_access_enabled = var.acr_public_network_access_enabled
  tags                          = var.tags
}

resource "azurerm_kubernetes_cluster" "this" {
  name                              = "aks-${var.name_prefix}"
  location                          = var.location
  resource_group_name               = var.resource_group_name
  dns_prefix                        = "aks-${var.name_prefix}"
  kubernetes_version                = var.kubernetes_version
  role_based_access_control_enabled = true
  local_account_disabled            = true
  oidc_issuer_enabled               = true
  workload_identity_enabled         = true
  tags                              = var.tags

  default_node_pool {
    name                         = "system"
    vm_size                      = var.system_node_vm_size
    auto_scaling_enabled         = true
    min_count                    = var.system_node_min_count
    max_count                    = var.system_node_max_count
    node_count                   = var.system_node_min_count
    vnet_subnet_id               = var.aks_subnet_id
    only_critical_addons_enabled = var.only_critical_addons_enabled
    temporary_name_for_rotation  = "sysrot"
    os_disk_type                 = "Managed"
  }

  identity {
    type = "SystemAssigned"
  }

  azure_active_directory_role_based_access_control {
    azure_rbac_enabled = true
    tenant_id          = data.azurerm_client_config.current.tenant_id
  }

  network_profile {
    network_plugin = "azure"
    network_policy = "azure"
    service_cidr   = var.service_cidr
    dns_service_ip = var.dns_service_ip
  }

  api_server_access_profile {
    authorized_ip_ranges = var.authorized_ip_ranges
  }

  oms_agent {
    log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  }

  key_vault_secrets_provider {
    secret_rotation_enabled = true
  }

  lifecycle {
    ignore_changes = [
      default_node_pool[0].node_count,
    ]
  }
}

resource "azurerm_kubernetes_cluster_node_pool" "workload" {
  count                 = var.enable_workload_node_pool ? 1 : 0
  name                  = "workload"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.this.id
  vm_size               = var.workload_node_vm_size
  auto_scaling_enabled  = true
  min_count             = var.workload_min_count
  max_count             = var.workload_max_count
  node_count            = var.workload_min_count
  vnet_subnet_id        = var.aks_subnet_id
  node_labels = {
    "workload" = "tradeflow-apps"
  }
  tags = var.tags

  lifecycle {
    ignore_changes = [
      node_count,
    ]
  }
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.this.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
}

resource "azurerm_user_assigned_identity" "tradeflow_api" {
  name                = "id-tradeflow-api-${var.name_prefix}"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_federated_identity_credential" "tradeflow_api" {
  name                      = "fic-tradeflow-api-${var.name_prefix}"
  user_assigned_identity_id = azurerm_user_assigned_identity.tradeflow_api.id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = azurerm_kubernetes_cluster.this.oidc_issuer_url
  subject                   = "system:serviceaccount:default:tradeflow-api-sa"
}

resource "azurerm_role_assignment" "tradeflow_api_kv_secrets_user" {
  scope                = var.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.tradeflow_api.principal_id
}
