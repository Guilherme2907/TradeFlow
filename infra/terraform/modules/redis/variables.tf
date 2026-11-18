variable "name_prefix" {
  type        = string
  description = "Naming prefix applied to Redis resources (e.g., tradeflow-dev)."
}

variable "location" {
  type        = string
  description = "Azure region where Azure Cache for Redis is deployed."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group hosting Redis and its Private Endpoint."
}

variable "vnet_id" {
  type        = string
  description = "Virtual network ID linked to the Redis Private DNS Zone."
}

variable "private_endpoints_subnet_id" {
  type        = string
  description = "Subnet ID hosting the Redis Private Endpoint."
}

variable "sku_name" {
  type        = string
  description = "Redis cache tier: Basic, Standard, or Premium."

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.sku_name)
    error_message = "sku_name must be Basic, Standard, or Premium."
  }
}

variable "capacity" {
  type        = number
  description = "Cache size index (0-6 for Basic/Standard C-family, 1-5 for Premium P-family)."
}

variable "tags" {
  type        = map(string)
  description = "Standard governance tags applied to all resources."
  default     = {}
}
