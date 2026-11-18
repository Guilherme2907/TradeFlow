variable "name_prefix" {
  type        = string
  description = "Naming prefix applied to PostgreSQL resources (e.g., tradeflow-dev)."
}

variable "location" {
  type        = string
  description = "Azure region where PostgreSQL Flexible Server is deployed."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group hosting the PostgreSQL server and private DNS zone."
}

variable "vnet_id" {
  type        = string
  description = "Virtual network ID linked to the PostgreSQL Private DNS Zone."
}

variable "delegated_subnet_id" {
  type        = string
  description = "Delegated subnet ID dedicated to PostgreSQL Flexible Server."
}

variable "administrator_login" {
  type        = string
  description = "PostgreSQL administrator username."
  default     = "tradeflow_admin"
}

variable "sku_name" {
  type        = string
  description = "PostgreSQL Flexible Server compute SKU (e.g., B_Standard_B2s or GP_Standard_D4s_v3)."
}

variable "storage_mb" {
  type        = number
  description = "Allocated PostgreSQL storage in megabytes."
  default     = 32768
}

variable "backup_retention_days" {
  type        = number
  description = "Backup retention period in days (7-35)."
  default     = 7
}

variable "geo_redundant_backup_enabled" {
  type        = bool
  description = "Enables geo-redundant backups across paired Azure regions."
  default     = false
}

variable "high_availability_enabled" {
  type        = bool
  description = "Enables Zone-Redundant High Availability standby server."
  default     = false
}

variable "tags" {
  type        = map(string)
  description = "Standard governance tags applied to all resources."
  default     = {}
}
