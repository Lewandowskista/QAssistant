// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

namespace QAssistant.Services
{
    /// <summary>
    /// Provides comprehensive SAP Commerce (Hybris) domain context for AI prompt enrichment.
    /// When enabled via settings, this context is appended to Gemini prompts so the model
    /// can generate more accurate, platform-aware test cases and analyses.
    /// </summary>
    public static class SapCommerceContext
    {
        /// <summary>
        /// Returns whether the SAP Commerce context toggle is enabled in settings.
        /// The toggle is stored as a local app setting (not per-project).
        /// </summary>
        public static bool IsEnabled()
        {
            var value = StorageService.Instance.GetSetting("SapCommerceContextEnabled");
            return value == "true";
        }

        /// <summary>
        /// Returns the full SAP Commerce domain context in TOON-compatible format
        /// for injection into Gemini prompts. Returns null when the toggle is off.
        /// </summary>
        public static string? GetContextBlock()
        {
            if (!IsEnabled())
                return null;

            return ContextBlock;
        }

        private const string ContextBlock = """
            sap_commerce_context{
            @hint:use_this_domain_knowledge_to_generate_platform_aware_test_cases_and_analyses_specific_to_SAP_Commerce

            platform_overview{
             name:SAP Commerce Cloud (formerly Hybris)
             type:enterprise_e-commerce_platform
             architecture:modular_extension_based_Java_Spring_framework
             deployment:SAP Commerce Cloud (CCv2)_or_on-premise
             database:HSQLDB(dev)|MySQL|Oracle|SAP_HANA(production)
             build:Apache_Ant_based_with_platform_build_framework
             frontend:Composable_Storefront(Spartacus_Angular)|JSP_Accelerator(legacy)|SmartEdit_WYSIWYG
             versions:SAP_Commerce_2211_2205_2105_2011_1905_with_patch_releases
             java:JDK_17_required_for_2211+|JDK_11_for_older
             license:SAP_proprietary_commercial_license
            }

            core_platform{
             type_system{
              definition:items.xml_defines_data_model_per_extension
              item_types:ItemType_ComposedType_CollectionType_MapType_EnumType_RelationType_AtomicType
              attributes:each_ItemType_has_typed_attributes_with_qualifiers_persistence_modifiers
              relations:one_to_many_many_to_many_with_source_target_cardinality_ordered_or_unordered
              deployment_table:each_type_maps_to_DB_table_via_deployment_tag_with_typecode
              jalo_layer:legacy_Jalo_items_deprecated_in_favor_of_Model_ServiceLayer
              generated_models:ant_build_generates_*Model.java_from_items.xml
              dynamic_attributes:DynamicAttributeHandler_for_computed_non_persisted_attributes
              enum_types:HybrisEnumValue_extensible_enumerations_with_dynamic_values
              atomic_types:map_to_Java_primitives_or_wrapper_classes
              localized_attributes:per_Locale_storage_via_LocalizedRow_tables
             }

             service_layer{
              pattern:DefaultService_with_DAO_Model_Interceptor_Converter_Populator
              model_service:ModelService_save_create_remove_refresh_attach_detach
              flexible_search:FlexibleSearchService_HQL_like_queries_on_type_system
              flexible_search_syntax:SELECT_pk_FROM_Type_WHERE_attribute_=_?param_ORDER_BY_attribute
              paging:FlexibleSearchQuery_with_setStart_setCount_for_pagination
              session_service:SessionService_for_user_session_management_current_user_currency_language
              i18n_service:I18NService_CommonI18NService_for_locale_currency_language_management
              user_service:UserService_for_user_CRUD_password_management_user_groups
              media_service:MediaService_for_media_CRUD_upload_download_URL_generation
              catalog_service:CatalogService_CatalogVersionService_for_catalog_management
              config_service:ConfigurationService_for_reading_properties_files
             }

             interceptors{
              prepare:PrepareInterceptor_runs_before_save_to_modify_model_state
              validate:ValidateInterceptor_runs_before_save_to_enforce_business_rules
              init_defaults:InitDefaultsInterceptor_sets_default_values_on_new_items
              load:LoadInterceptor_runs_after_loading_item_from_DB
              remove:RemoveInterceptor_runs_before_item_deletion_for_cleanup
              registration:interceptors_registered_via_Spring_XML_with_typeCode_mapping
              ordering:interceptors_can_be_ordered_via_order_attribute
             }

             events{
              event_service:EventService_publishEvent_registerEventListener
              built_in:AfterItemCreationEvent_BeforeItemRemovalEvent_AfterItemSaveEvent
              cluster_aware:ClusterAwareEvent_for_multi_node_propagation
              transaction_aware:TransactionAwareEvent_fires_after_commit
              custom_events:extend_AbstractEvent_register_via_Spring
             }

             cronjobs{
              model:CronJobModel_with_Trigger_for_scheduling
              performable:AbstractJobPerformable_with_perform_method_returning_CronJobResult
              trigger:TriggerModel_with_cron_expression_or_interval
              clustering:CronJobService_distributes_jobs_across_cluster_nodes
              monitoring:CronJobHistoryModel_tracks_execution_history_status_duration
              abort:isAbortable_flag_allows_manual_abort_via_Backoffice_or_HAC
              composite:CompositeCronJob_chains_multiple_CronJobs_in_sequence
              scripting:ScriptingJob_allows_Groovy_Beanshell_script_execution
             }

             impex{
              modes:INSERT|UPDATE|INSERT_UPDATE|REMOVE
              header:specifies_type_and_attribute_mappings_with_modifiers
              modifiers:translator|default|unique|mandatory|forceWrite|allownull|batchmode|lang
              macros:$variable=value_for_reuse_in_headers
              translators:custom_ImpExTranslator_implementations_for_complex_data_mapping
              media_import:import_binary_data_via_media_references_zip_archive
              distributed:DistributedImpEx_for_parallel_processing_of_large_files
              validation:ImpExValidationMode_strict_or_relaxed
              hot_folder:HotFolderService_automated_file_based_import_with_mapping_configuration
             }

             multi_tenancy{
              tenants:master_tenant_plus_additional_isolated_tenants
              isolation:each_tenant_has_own_DB_schema_type_system_configuration
              use_case:shared_infrastructure_with_data_isolation_for_multi_brand
             }
            }

            commerce_module{
             cart{
              model:CartModel_extends_AbstractOrderModel
              service:CartService_CommerceCartService_for_cart_operations
              entries:CartEntryModel_with_product_quantity_basePrice_totalPrice
              entry_groups:EntryGroupModel_for_logical_grouping_of_entries
              calculation:CalculationService_recalculates_totals_taxes_discounts
              multi_cart:multiple_carts_per_user_saved_carts_wishlist_pattern
              session_cart:session_scoped_anonymous_cart_merged_on_login
              cart_hooks:CommerceCartCalculationMethodHook_for_custom_calculation_logic
              cart_validation:CommerceCartValidationStrategy_validates_stock_prices_on_checkout
             }

             checkout{
              facade:CheckoutFacade_AcceleratorCheckoutFacade_for_multistep_flow
              steps:delivery_address|delivery_mode|payment_info|review_and_place_order
              payment:PaymentInfoModel_CardInfo_or_InvoicePaymentInfo(B2B)
              delivery:DeliveryModeModel_ZoneDeliveryMode_FlatRateDeliveryMode
              address:AddressModel_with_country_region_city_postal_code_phone
              guest_checkout:anonymous_checkout_with_email_based_order_tracking
              express_checkout:single_step_checkout_using_default_address_and_payment
              place_order:creates_OrderModel_from_CartModel_triggers_order_process
             }

             order{
              model:OrderModel_extends_AbstractOrderModel
              lifecycle:CREATED|CHECKED_VALID|PAYMENT_AUTHORIZED|PAYMENT_CAPTURED|READY|COMPLETED|CANCELLED|SUSPENDED
              process:OrderProcess_business_process_engine_with_action_nodes_wait_states
              entries:OrderEntryModel_with_entryNumber_quantity_product_prices
              consignment:ConsignmentModel_for_split_shipments_with_ConsignmentEntry
              returns:ReturnRequestModel_RefundEntryModel_for_RMA_processing
              cancel:OrderCancelService_partial_or_full_cancellation_with_cancel_entries
              history:OrderHistoryEntry_tracks_status_changes
              fraud:FraudService_fraud_detection_scoring_before_payment_capture
              splitting:OrderSplittingService_splits_order_into_consignments_by_warehouse
             }

             pricing{
              engine:Europe1PricingService_PDT(Price_Discount_Tax)_system
              price_rows:PriceRowModel_with_currency_unit_net_date_range_user_group
              discounts:DiscountModel_DiscountRowModel_percentage_or_absolute
              taxes:TaxModel_TaxRowModel_country_region_product_group_based
              price_groups:UserPriceGroup_ProductPriceGroup_for_group_based_pricing
              global_discounts:GlobalDiscountRow_applied_to_all_products
              date_ranges:dateRange_on_PriceRow_for_time_limited_pricing
              quantity_pricing:PriceRow_with_minQuantity_for_volume_discounts
              net_gross:net_gross_flag_on_PriceRow_and_Currency_for_tax_inclusive_exclusive
              rounding:rounding_mode_configurable_per_currency_HALF_UP_HALF_DOWN
             }

             promotions{
              engine:PromotionEngineService_rule_based_with_Drools
              source_rules:SourceRule_with_conditions_and_actions_defined_in_Backoffice
              promotion_groups:PromotionGroup_for_site_specific_rule_sets
              conditions:cart_total|product_quantity|category|customer_group|date_range|coupon
              actions:percentage_discount|fixed_discount|free_gift|fixed_price|free_shipping|partner_product
              coupons:SingleCodeCoupon_MultiCodeCoupon_generation_and_redemption
              priority:rule_priority_and_exclusivity_for_conflict_resolution
              rule_engine:RuleEngineService_compiles_SourceRules_to_Drools_DRL
              promotion_result:PromotionResult_PromotionOrderEntryConsumed_tracking
             }

             stock{
              service:StockService_CommerceStockService_for_availability_checks
              model:StockLevelModel_with_available_reserved_preOrder_overSelling
              warehouse:WarehouseModel_PointOfServiceModel_for_location_based_stock
              status:StockLevelStatus_inStock_lowStock_outOfStock
              allocation:AllocationService_reserves_stock_on_order_placement
              atp:AvailableToPromise_calculation_considering_reserved_and_threshold
              event:StockLevelEvent_for_real_time_stock_change_notifications
              backorder:forceInStock_flag_allows_ordering_without_stock
             }

             catalog{
              model:CatalogModel_with_CatalogVersionModel_Staged_and_Online
              synchronization:CatalogVersionSyncJob_syncs_Staged_to_Online
              sync_config:SyncItemJob_with_include_exclude_rules_rootTypes
              versioning:Staged_for_content_authoring_Online_for_production
              multi_catalog:product_catalog_content_catalog_classification_catalog
              catalog_aware:CatalogUnawareItems_vs_CatalogVersionAwareItems
              sync_conflicts:ConflictResolutionStrategy_for_merge_conflicts
             }

             products{
              model:ProductModel_with_code_name_description_EAN_approvalStatus
              variants:VariantProductModel_GenericVariantProduct_with_variant_type_hierarchy
              apparel:ApparelStyleVariantProduct_ApparelSizeVariantProduct_for_fashion
              electronics:ElectronicsColorVariantProduct_for_electronics_storefront
              classification:ClassificationClass_ClassificationAttribute_for_product_features
              features:ProductFeatureModel_typed_feature_values_per_classification
              media:ProductModel_has_picture_thumbnail_galleryImages_via_MediaContainer
              references:ProductReference_for_cross_sell_up_sell_similar_accessories
              approval:ApprovalStatus_check_approved_unapproved_for_publication_control
              unit:UnitModel_for_quantity_units_conversion_factors
             }

             categories{
              model:CategoryModel_hierarchical_with_supercategories_subcategories
              catalog_aware:categories_belong_to_CatalogVersion
              products:CategoryModel_has_many_products_via_CategoryProductRelation
              classification:ClassificationClassModel_extends_CategoryModel
              navigation:categories_used_for_storefront_navigation_and_facet_filtering
             }

             customers{
              b2c:CustomerModel_extends_UserModel_with_customerID_session_cart
              b2b:B2BCustomerModel_extends_CustomerModel_with_unit_roles_permissions
              registration:RegistrationService_with_email_verification
              address_book:AddressModel_linked_to_user_for_shipping_billing
              consent:ConsentModel_ConsentTemplate_for_GDPR_compliance
              customer_groups:UserGroupModel_PriceGroup_DiscountGroup_TaxGroup
              saved_carts:CommerceSaveCartService_for_wishlist_reorder_patterns
             }
            }

            b2b_commerce{
             organization{
              model:B2BUnitModel_hierarchical_organizational_structure
              customers:B2BCustomerModel_with_roles(b2badmingroup,b2bcustomergroup,b2bmanagergroup)
              addresses:B2BUnit_has_shipping_billing_addresses_inherited_by_members
              cost_centers:B2BCostCenterModel_assigned_to_unit_for_budget_tracking
             }

             approval{
              process:B2BApprovalProcess_workflow_engine_based
              permissions:B2BOrderThresholdPermission|B2BOrderThresholdTimespanPermission|B2BBudgetExceededPermission
              approvers:B2BApproverModel_assigned_to_unit_or_user
              escalation:multi_level_approval_chain_with_timeout_escalation
              status:PENDING_APPROVAL|APPROVED|REJECTED|APPROVAL_TIMEOUT
             }

             budgets{
              model:B2BBudgetModel_with_budget_amount_currency_dateRange
              cost_center:B2BCostCenterModel_links_budget_to_organizational_unit
              tracking:budget_consumed_vs_available_per_period
              exceeded:B2BBudgetExceededPermission_triggers_approval_when_over_budget
             }

             quotes{
              model:QuoteModel_with_state_machine_lifecycle
              states:BUYER_DRAFT|BUYER_SUBMITTED|SELLER_SUBMITTED|BUYER_ACCEPTED|BUYER_ORDERED|CANCELLED|EXPIRED
              negotiation:QuoteDiscount_applied_at_entry_or_header_level
              expiry:QuoteExpirationTime_auto_expire_after_configurable_period
              comments:QuoteComment_for_buyer_seller_communication
             }

             purchase_orders{
              payment_type:purchase_order_as_payment_method_for_B2B
              approval_flow:integrates_with_B2BApprovalProcess
              account_payment:ACCOUNT_payment_type_for_invoice_based
             }

             order_forms{
              quick_order:QuickOrderForm_for_bulk_SKU_entry
              reorder:reorder_from_previous_order_history
              csv_upload:CSV_file_upload_for_bulk_product_addition
             }
            }

            content_management{
             cms{
              site:CMSSiteModel_with_stores_catalogs_languages_currencies_theme
              page:ContentPageModel_CategoryPageModel_ProductPageModel_with_template
              slots:ContentSlotModel_named_container_for_components_per_page_or_template
              components:AbstractCMSComponentModel_hierarchy(BannerComponent,CMSParagraphComponent,ProductCarouselComponent,NavigationComponent,MiniCartComponent,SearchBoxComponent,CategoryNavigationComponent,FooterNavigationComponent,CMSFlexComponent)
              slot_for_page:ContentSlotForPageModel_page_specific_slot_override
              slot_for_template:ContentSlotForTemplateModel_template_level_slot_default
             }

             smartedit{
              editor:WYSIWYG_page_editor_iframe_based_with_decorator_pattern
              components:drag_drop_component_placement_in_slots
              slots:slot_visualization_add_remove_components_in_preview
              personalization:SmartEdit_personalization_widget_for_variation_management
              workflow:page_approval_workflow_submit_approve_reject_publish
              versioning:page_version_history_compare_rollback
              permissions:SmartEdit_read_write_permissions_per_catalog_version
             }

             page_templates{
              model:PageTemplateModel_defines_layout_structure
              content_slots:ContentSlotName_defined_in_template_with_allowed_component_types
              frontend:template_maps_to_Angular_route_or_JSP_layout
              responsive:mobile_tablet_desktop_adaptive_via_CSS_grid
             }

             restrictions{
              time:CMSTimeRestriction_active_from_active_until
              catalog:CMSCatalogRestriction_catalog_version_based
              user_group:CMSUserGroupRestriction_show_hide_for_user_groups
              category:CMSCategoryRestriction_category_based_component_visibility
              inverse:useAsExclude_flag_for_inverse_restriction_logic
             }

             navigation{
              node:NavigationNodeModel_hierarchical_tree_structure
              entry:NavigationEntryModel_links_to_page_category_URL
              component:NavigationComponent_renders_navigation_tree_in_storefront
              footer:FooterNavigationComponent_for_footer_links
              breadcrumb:BreadcrumbComponent_based_on_category_hierarchy
             }

             media{
              model:MediaModel_with_mime_type_size_realFileName_URL
              container:MediaContainerModel_groups_responsive_formats_together
              formats:MediaFormatModel(thumbnail,mobile,tablet,desktop,zoom,widescreen)
              folder:MediaFolderModel_for_organization
              cdn:CDN_URL_generation_for_production_media_delivery
              upload:MediaService_for_programmatic_upload_setStreamForMedia
             }
            }

            search_and_navigation{
             solr{
              service:SolrFacetSearchService_for_search_and_facet_queries
              config:FacetSearchConfig_defines_indexed_types_server_connection
              indexed_type:SolrIndexedTypeModel_maps_Commerce_type_to_Solr_core
              indexed_property:SolrIndexedPropertyModel_defines_searchable_attributes_with_type_facet_sort_boost
              value_provider:FieldValueProvider_custom_logic_to_extract_index_values
              value_range:SolrValueRange_for_range_facets(price_ranges,rating_ranges)
             }

             indexing{
              jobs:SolrIndexerJob_with_FULL_UPDATE_DELETE_modes
              strategy:indexing_strategy_direct_or_queued
              hot_update:SolrIndexerHotUpdateListener_for_real_time_partial_updates
              languages:multi_language_indexing_with_language_specific_fields
              currencies:multi_currency_indexing_for_price_filtering
             }

             facets{
              data:FacetSearchPageData_contains_results_facets_breadcrumbs_pagination
              facet_value:FacetValueData_with_count_selected_state
              refinement:facet_refinement_narrows_search_results
              multi_select:multi_select_OR_logic_within_facet_single_select_AND_across_facets
              sort:SolrSort_defines_sort_options(relevance,topRated,name,price)
             }

             search_profiles{
              model:AbstractSolrSearchProfileModel_CategoryAwareSolrSearchProfile_GlobalSolrSearchProfile
              boost_rules:SolrBoostRule_promotes_products_matching_criteria
              facet_config:SolrSearchProfileFacetConfiguration_overrides_default_facet_behavior
              category_aware:CategoryAwareSolrSearchProfile_applies_rules_per_category_page
             }

             adaptive_search{
              as_module:AdaptiveSearch_replaces_legacy_search_profiles
              as_config:AsSearchConfiguration_with_promoted_excluded_facet_boost_sort
              category_aware:category_specific_merchandising_rules
              global:AsGlobalSearchProfile_applies_across_all_categories
             }
            }

            integration{
             occ_api{
              type:OmniCommerce_Channel_RESTful_API_v2
              base_path:/occ/v2/{baseSiteId}
              endpoints:products|carts|users|orders|catalogs|stores|cms|promotions|vouchers|consents|addresses|deliverymodes|paymentdetails|returns
              authentication:OAuth2_client_credentials_or_resource_owner_password_grant
              format:JSON_response_with_configurable_field_level_expansion
              versioning:v2_is_current_v1_deprecated
              customization:extend_via_WsDTO_Populator_Controller_annotations
              cors:CORS_configuration_for_storefront_domain_whitelisting
              rate_limiting:configurable_per_client_rate_limits
              error_handling:ErrorWsDTO_with_type_message_reason_for_API_errors
             }

             integration_api{
              type:OData_v2_REST_API_for_system_integration
              use_case:ERP_PIM_DAM_middleware_integration
              authentication:OAuth2_with_InboundChannelConfiguration
              inbound:InboundChannelConfiguration_defines_exposed_integration_objects
              outbound:OutboundChannelConfiguration_sends_data_to_external_systems_on_events
              monitoring:IntegrationMonitoring_for_payload_tracking_and_error_analysis
              filtering:OData_$filter_$expand_$orderby_query_parameters
             }

             sap_erp{
              s4hana:integration_with_SAP_S/4HANA_for_pricing_stock_orders
              rfc:RFC_BAPI_calls_via_JCo_SAP_Java_Connector
              idoc:IDoc_document_exchange_for_asynchronous_data_sync
              cpi:SAP_BTP_Integration_Suite(CPI)_iFlows_for_mediated_integration
              data_sync:product_master|customer_master|pricing_conditions|stock_levels|order_replication
             }

             kyma{
              runtime:SAP_BTP_Kyma_Runtime_Kubernetes_based
              events:EventMesh_for_event_driven_extensions
              api_rules:APIRule_for_exposing_custom_microservice_endpoints
              use_case:custom_logic_side_car_extensions_without_modifying_core
             }

             webhooks{
              outbound:WebhookConfiguration_for_real_time_event_notification
              events:item_saved|item_removed|order_status_change
              payload:JSON_payload_with_item_data
              retry:retry_policy_with_exponential_backoff
             }
            }

            personalization{
             module{
              service:CxPersonalizationService_for_segment_based_content_targeting
              customization:CxCustomizationModel_groups_variations_targeting_segments
              variation:CxVariationModel_defines_content_changes_for_a_segment
              trigger:CxSegmentTriggerModel_CxExpressionTriggerModel_for_segment_activation
             }

             segments{
              model:CxSegmentModel_user_segment_calculated_from_profile_data
              calculation:CxCalculationService_evaluates_segment_membership
              providers:CxUserSegmentProvider_custom_segment_calculation_logic
              commerce_segments:cart_value|product_category_interest|order_history|geographic_location
             }

             actions{
              cms:CxCmsAction_replaces_CMS_component_in_slot_for_segment
              search:CxSearchProfileAction_applies_search_profile_for_segment
              promotion:CxPromotionAction_activates_promotion_for_segment
             }

             context{
              service:CxContextService_manages_personalization_session_data
              attributes:session_attributes_user_profile_data_browsing_history
              recalculation:segment_recalculation_on_profile_change
             }
            }

            backoffice{
             framework{
              technology:ZK_framework_based_web_application
              widgets:widget_based_UI_with_widget.xml_definition
              cockpit_ng:CockpitNG_framework_for_backoffice_customization
              perspectives:custom_perspectives_and_navigation_areas
             }

             administration{
              hac:Hybris_Administration_Console_for_platform_management
              hac_features:FlexibleSearch_console|ImpEx_import_export|Monitoring|Configuration|Maintenance
              backoffice:Backoffice_for_business_user_content_catalog_management
              user_management:create_edit_users_groups_roles_permissions_in_Backoffice
             }

             pcm{
              product_cockpit:Product_Content_Management_in_Backoffice
              bulk_edit:BulkEditWizard_for_mass_product_attribute_updates
              workflow:product_approval_workflow_draft_pending_approved
              excel_import:Excel_import_export_for_product_data
             }

             order_management{
              oms:Order_Management_module_in_Backoffice
              fulfillment:consignment_based_fulfillment_with_sourcing_strategy
              sourcing:SourcingService_determines_warehouse_for_fulfillment
              returns:ReturnProcess_business_process_for_return_handling
              fraud_review:manual_fraud_review_queue_in_Backoffice
             }

             rule_builder{
              promotion_rules:visual_rule_builder_for_promotion_conditions_and_actions
              source_rules:SourceRule_editor_with_condition_action_groups
              rule_templates:RuleTemplate_predefined_patterns_for_common_promotions
              rule_deployment:publish_rules_to_DroolsEngine_module
             }
            }

            security{
             authentication{
              oauth2:OAuth2_token_based_with_Spring_Security
              grant_types:client_credentials|password|authorization_code
              token_endpoint:/authorizationserver/oauth/token
              client_details:OAuthClientDetailsModel_with_scope_authorities_grant_types
              session:HttpSession_or_stateless_JWT_for_storefront
             }

             authorization{
              rbac:role_based_access_control_via_UserGroupModel
              groups:admingroup|employeegroup|customergroup|b2badmingroup|b2bcustomergroup|b2bmanagergroup
              access_rights:read_write_create_remove_per_type_and_attribute
              restrictions:SearchRestriction_limits_FlexibleSearch_results_by_user_group
              backoffice_perms:CockpitNG_read_write_permissions_per_perspective
             }

             protection{
              csrf:CSRFFilter_token_validation_on_POST_PUT_DELETE
              xss:XSSFilter_output_encoding_Content_Security_Policy
              cors:CorsFilter_configuration_for_allowed_origins_methods_headers
              clickjacking:X_Frame_Options_header_DENY_or_SAMEORIGIN
              sql_injection:FlexibleSearch_parameterized_queries_prevent_injection
              password:PasswordEncoder_BCrypt_or_PBKDF2_with_configurable_policy
             }

             audit{
              logging:AuditChangeFilter_tracks_create_update_delete_operations
              audit_report:AuditReportService_generates_audit_trail_for_compliance
              gdpr:GDPR_erasure_service_for_customer_data_right_to_be_forgotten
              consent:ConsentModel_ConsentTemplate_for_tracking_user_consent
             }
            }

            testing_considerations{
             unit_testing{
              framework:JUnit_5_or_JUnit_4_with_Mockito
              base_classes:ServicelayerTest_ServicelayerTransactionalTest
              model_mocking:mock_ModelService_FlexibleSearchService_for_unit_tests
              interceptor_tests:verify_interceptor_logic_in_isolation
             }

             integration_testing{
              platform_required:tests_require_running_SAP_Commerce_platform
              tenant:tests_run_within_JUnit_tenant
              impex_setup:test_data_loaded_via_ImpEx_files_in_resources_test
              transactional:ServicelayerTransactionalTest_rolls_back_after_each_test
              spring_context:test_Spring_context_loaded_from_extension_spring.xml
             }

             api_testing{
              occ:REST_API_testing_via_REST_Assured_Postman_or_HTTP_client
              contract:API_contract_validation_against_OCC_specification
              auth:test_OAuth2_token_flow_client_credentials_password_grant
              error_scenarios:test_4xx_5xx_responses_invalid_input_missing_auth
              performance:response_time_SLA_verification_under_load
             }

             storefront_testing{
              framework:Cypress_Playwright_for_Spartacus_Composable_Storefront_E2E
              key_flows:PLP_search_PDP_add_to_cart_checkout_order_confirmation_my_account
              responsive:test_mobile_tablet_desktop_viewport_breakpoints
              accessibility:WCAG_2.1_AA_compliance_testing
              i18n:multi_language_multi_currency_locale_specific_formatting
              seo:canonical_URLs_meta_tags_structured_data_sitemap
             }

             backoffice_testing{
              manual:Backoffice_widget_testing_via_browser_interaction
              automation:Selenium_WebDriver_for_Backoffice_UI_automation
              impex:ImpEx_import_export_validation_in_HAC
              cronjob:CronJob_execution_monitoring_in_HAC_or_Backoffice
             }

             performance_testing{
              tools:JMeter_Gatling_Locust_for_load_testing
              key_scenarios:homepage|PLP|PDP|search|add_to_cart|checkout|order_placement
              metrics:response_time_throughput_error_rate_concurrent_users
              caching:test_cache_hit_miss_ratio_with_Varnish_CDN
              solr:search_query_performance_indexing_duration
             }

             data_integrity{
              catalog_sync:verify_Staged_to_Online_sync_correctness_no_data_loss
              price_consistency:verify_price_rows_currency_date_range_quantity_breaks
              stock_accuracy:verify_stock_levels_match_warehouse_reality
              order_totals:verify_cart_calculation_rounding_tax_discount_accuracy
              media_links:verify_media_URLs_resolve_no_broken_images
             }

             common_defects{
              catalog:catalog_sync_failures|partial_sync_data_loss|version_conflicts
              pricing:price_calculation_errors|currency_conversion_rounding|tax_miscalculation|discount_stacking_issues
              promotions:promotion_rule_conflicts|coupon_redemption_failures|free_gift_not_applied|rule_compilation_errors
              search:solr_index_stale_data|search_returns_out_of_stock|facet_count_mismatch|indexer_timeout
              cart:cart_calculation_rounding|entry_group_inconsistency|session_cart_merge_failures|multi_cart_conflicts
              api:OCC_API_contract_violations|missing_fields_in_response|auth_token_expiry_handling|CORS_errors
              impex:impex_import_failures|missing_required_attributes|encoding_issues|header_mismatch
              cronjobs:cronjob_timeout|stuck_running_state|cluster_node_affinity_issues|trigger_misconfiguration
              session:session_timeout_handling|sticky_session_failover|anonymous_to_authenticated_transition
              multi_site:multi_site_multi_currency_edge_cases|wrong_catalog_version_context|language_fallback_errors
              b2b:approval_workflow_stuck|budget_calculation_errors|unit_hierarchy_permission_inheritance
              performance:N+1_query_patterns|cache_miss_storms|solr_slow_queries|large_cart_performance
             }
            }

            deployment_ccv2{
             environments{
              types:Development(d1)|Staging(s1)|Production(p1)_with_endpoint_configuration
              endpoints:Storefront|Backoffice|API|SmartEdit_per_environment
              properties:environment_specific_properties_via_Cloud_Portal_or_manifest
              data:separate_DB_per_environment_with_data_migration_for_promotion
             }

             builds{
              manifest:manifest.json_defines_extensions_addons_aspects_webapps_properties
              aspects:accstorefront|backoffice|backgroundProcessing_define_deployment_aspects
              extensions:list_of_required_extensions_per_aspect
              addons:storefront_addons_installed_via_ant_addoninstall
              repository:Git_repository_with_custom_extensions_and_configuration
              ci_cd:build_triggered_via_Cloud_Portal_or_API_with_build_code
             }

             operations{
              hot_folder:automated_file_based_data_import_via_cloud_storage
              monitoring:Dynatrace_integration_for_APM_SAP_Cloud_ALM
              blue_green:zero_downtime_deployment_with_rolling_update
              scaling:horizontal_scaling_with_auto_scaling_policies
              backup:automated_DB_backup_with_point_in_time_recovery
              logging:centralized_logging_via_Kibana_OpenSearch
              maintenance:maintenance_page_during_data_migration_or_updates
             }

             datahub{
              portal:SAP_Commerce_Cloud_Portal_for_build_deploy_monitor
              builds:view_build_status_logs_artifacts
              deployments:deploy_to_environment_with_data_migration_option
              endpoints:manage_public_endpoints_SSL_certificates
             }
            }

            key_configurations{
             properties{
              files:local.properties_project.properties_localextensions.xml
              override_order:platform_defaults<extension_properties<local.properties<environment_properties
              common:db.url|db.driver|db.username|db.password|installed.tenants|cluster.id
              performance:cache.main|regioncache.stats|cronjob.maxthreads|task.engine.loadonstartup
             }

             spring{
              config:extension-specific_Spring_XML_in_resources/extension-spring.xml
              override:bean_override_via_alias_or_same_id_in_extension_spring.xml
              profiles:Spring_profiles_for_environment_specific_beans
              aop:AOP_aspects_for_cross_cutting_concerns
             }

             solr{
              config:solrconfig.xml_schema.xml_per_indexed_type
              cores:separate_Solr_core_per_indexed_type_and_language
              replication:Solr_master_slave_or_SolrCloud_for_production
              tuning:commitWithin|autoSoftCommit|maxBufferedDocs_for_performance
             }

             localization{
              languages:LanguageModel_with_isocode_active_flag
              currencies:CurrencyModel_with_isocode_symbol_digits_conversion
              countries:CountryModel_RegionModel_for_address_validation
              date_format:locale_specific_date_number_formatting
              fallback:language_fallback_chain_for_missing_translations
             }

             multi_site{
              base_site:BaseSiteModel_CMSSiteModel_with_stores_catalogs_urls
              url_patterns:regex_based_URL_to_site_mapping
              catalogs:site_specific_product_and_content_catalogs
              themes:site_specific_theme_and_styling
              stores:BaseStoreModel_with_delivery_countries_currencies_languages_payment_providers
             }

             caching{
              regions:region_cache_configuration_for_type_system_queries
              keys:cache_key_strategy_for_OCC_responses
              invalidation:cache_invalidation_on_item_save_catalog_sync
              external:Varnish_CDN_caching_for_storefront_pages_and_media
             }
            }

            composable_storefront{
             spartacus{
              framework:Angular_based_Composable_Storefront
              version:Spartacus_6.x_7.x_with_Angular_17+
              architecture:module_based_with_lazy_loading_feature_libraries
              state:NgRx_store_for_state_management
              cms:CMS_driven_page_structure_loaded_via_OCC_API
              routing:configurable_routing_with_semantic_URLs
              i18n:Angular_i18n_with_translation_chunks_lazy_loaded
              ssr:Angular_Universal_server_side_rendering_for_SEO
              pwa:Progressive_Web_App_capabilities_with_service_worker
             }

             customization{
              components:extend_or_replace_CMS_component_mappings
              services:override_Angular_services_via_DI_providers
              modules:feature_modules_for_B2B_CDC_ASM_Personalization
              styling:SCSS_variables_and_theme_customization
              layout:LayoutConfig_for_slot_component_mapping
             }

             asm{
              module:Assisted_Service_Module_for_customer_support_agents
              emulation:agent_can_emulate_customer_session_in_storefront
              features:cart_management|order_history|customer_360_view
             }
            }
            }
            """;
    }
}
