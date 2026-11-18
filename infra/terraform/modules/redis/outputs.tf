output "redis_id" {
  value       = azurerm_redis_cache.this.id
  description = "Resource ID of the Azure Cache for Redis instance."
}

output "hostname" {
  value       = azurerm_redis_cache.this.hostname
  description = "Private hostname of the Azure Cache for Redis instance."
}

output "ssl_port" {
  value       = azurerm_redis_cache.this.ssl_port
  description = "TLS port (6380) of the Azure Cache for Redis instance."
}

output "primary_connection_string" {
  value       = azurerm_redis_cache.this.primary_connection_string
  description = "Primary StackExchange.Redis TLS connection string (stored in Key Vault)."
  sensitive   = true
}
