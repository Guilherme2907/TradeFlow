terraform {
  backend "azurerm" {
    resource_group_name  = "rg-tradeflow-tfstate"
    storage_account_name = "sttradeflowtfstate"
    container_name       = "tfstate"
    key                  = "tradeflow-prod.tfstate"
    use_azuread_auth     = true
  }
}
