output "vnet_id" {
  value       = azurerm_virtual_network.this.id
  description = "Resource ID of the TradeFlow virtual network."
}

output "vnet_name" {
  value       = azurerm_virtual_network.this.name
  description = "Name of the TradeFlow virtual network."
}

output "aks_subnet_id" {
  value       = azurerm_subnet.aks.id
  description = "Subnet ID allocated for AKS nodes and Azure CNI pods."
}

output "postgresql_subnet_id" {
  value       = azurerm_subnet.postgresql.id
  description = "Delegated subnet ID isolated for PostgreSQL Flexible Server."
}

output "private_endpoints_subnet_id" {
  value       = azurerm_subnet.private_endpoints.id
  description = "Subnet ID hosting Private Endpoints for Redis and Key Vault."
}
