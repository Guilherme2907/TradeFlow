variable "vault_name" {
  type        = string
  description = "Globally unique Azure Key Vault name (3-24 alphanumeric characters or hyphens)."
}

variable "name_prefix" {
  type        = string
  description = "Naming prefix applied to Key Vault Private Endpoint resources."
}

variable "location" {
  type        = string
  description = "Azure region where the Key Vault is deployed."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group hosting the Key Vault."
}

variable "vnet_id" {
  type        = string
  description = "Virtual network ID linked to the Key Vault Private DNS Zone."
}

variable "private_endpoints_subnet_id" {
  type        = string
  description = "Private Endpoint subnet ID hosting the Key Vault private endpoint."
}

variable "allowed_subnet_ids" {
  type        = list(string)
  description = "Subnet IDs permitted through Key Vault network ACL service endpoints."
  default     = []
}

variable "allowed_ip_rules" {
  type        = list(string)
  description = "Public IP CIDR blocks permitted to access the Key Vault."
  default     = []
}

variable "public_network_access_enabled" {
  type        = bool
  description = "Controls public internet access to the Key Vault."
  default     = false
}

variable "purge_protection_enabled" {
  type        = bool
  description = "Protects secrets against permanent deletion during soft-delete retention."
  default     = true
}

variable "tags" {
  type        = map(string)
  description = "Standard governance tags applied to all resources."
  default     = {}
}
