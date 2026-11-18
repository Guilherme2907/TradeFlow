variable "name_prefix" {
  type        = string
  description = "Naming prefix applied to networking resources (e.g., tradeflow-dev)."
}

variable "location" {
  type        = string
  description = "Azure region where networking resources are deployed."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group hosting the virtual network and security groups."
}

variable "vnet_address_space" {
  type        = list(string)
  description = "CIDR block(s) allocated to the TradeFlow virtual network."
}

variable "aks_subnet_prefix" {
  type        = string
  description = "CIDR block for the AKS nodes and Azure CNI pods."
}

variable "postgresql_subnet_prefix" {
  type        = string
  description = "CIDR block for the delegated PostgreSQL Flexible Server subnet."
}

variable "private_endpoints_subnet_prefix" {
  type        = string
  description = "CIDR block for Azure Private Endpoints (Redis, Key Vault)."
}

variable "tags" {
  type        = map(string)
  description = "Standard governance tags applied to all resources."
  default     = {}
}
