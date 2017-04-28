﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web.UI.WebControls;

using Rock;
using Rock.Data;
using Rock.Model;
using Rock.Security;
using Rock.Web.UI;

namespace RockWeb.Plugins.com_shepherdchurch.Misc
{
    [DisplayName( "Trace Security" )]
    [Category( "Shepherd Church > Misc" )]
    [Description( "Trace the security of an entity and how it applies to a specified user." )]

    public partial class TraceSecurity : RockBlock
    {
        #region Base Method Overrides

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnInit( EventArgs e )
        {
            base.OnInit( e );

            // this event gets fired after block settings are updated. it's nice to repaint the screen if these settings would alter it
            this.BlockUpdated += Block_BlockUpdated;
            this.AddConfigurationUpdateTrigger( upnlContent );

            gResults.Actions.ShowMergeTemplate = false;
        }

        /// <summary>
        /// Initialize basic information about the page structure and setup the default content.
        /// </summary>
        /// <param name="sender">Object that is generating this event.</param>
        /// <param name="e">Arguments that describe this event.</param>
        protected void Page_Load( object sender, EventArgs e )
        {
            if ( !IsPostBack )
            {
                var rockContext = new RockContext();

                var entityTypes = new EntityTypeService( rockContext )
                    .GetEntities()
                    .Where( t => t.IsSecured )
                    .OrderBy( t => t.FriendlyName )
                    .ToList();
                pEntityType.EntityTypes = entityTypes;
            }
        }

        #endregion

        #region Core Methods

        /// <summary>
        /// Helper method to load an entity by it's type name and Guid.
        /// </summary>
        /// <param name="entityType">The class name of the entity.</param>
        /// <param name="guid">The Guid of the entity to load.</param>
        /// <returns>An entity object or null if not found.</returns>
        public IEntity GetByGuid( string entityType, Guid guid )
        {
            var type = Reflection.FindType( typeof( IEntity ), entityType );
            var context = Reflection.GetDbContextForEntityType( type );
            var service = Reflection.GetServiceForEntityType( type, context );

            if ( service != null )
            {
                var getMethod = service.GetType().GetMethod( "Get", new Type[] { typeof( Guid ) } );

                if ( getMethod != null )
                {
                    return ( IEntity ) getMethod.Invoke( service, new object[] { guid } );
                }
            }

            return null;
        }

        /// <summary>
        /// Helper method to load an entity by it's type name and Id number.
        /// </summary>
        /// <param name="entityType">The class name of the entity.</param>
        /// <param name="id">The Id number of the entity to load.</param>
        /// <returns>An entity object or null if not found.</returns>
        public IEntity GetById( string entityType, int id )
        {
            var type = Reflection.FindType( typeof( IEntity ), entityType );
            var context = Reflection.GetDbContextForEntityType( type );
            var service = Reflection.GetServiceForEntityType( type, context );

            if ( service != null )
            {
                var getMethod = service.GetType().GetMethod( "Get", new Type[] { typeof( int ) } );

                if ( getMethod != null )
                {
                    return ( IEntity ) getMethod.Invoke( service, new object[] { id } );
                }
            }

            return null;
        }

        /// <summary>
        /// Find the matching Auth record for this entity, action and person combination. Only explicit
        /// Auth records are checked.
        /// </summary>
        /// <param name="entity">The entity whose Auth records we are going to search for.</param>
        /// <param name="action">The type of action that is to be authenticated.</param>
        /// <param name="person">The person that is requesting permission.</param>
        /// <returns>An Auth object if an explicit permission was found, otherwise null.</returns>
        Auth MatchingAuth( ISecured entity, string action, Person person )
        {
            var rockContext = new RockContext();
            var authService = new AuthService( rockContext );

            var rules = authService.Get( entity.TypeId, entity.Id ).Where( a => a.Action == action ).ToList();
            bool matchFound = false;
            bool authorized = false;

            foreach ( var authRule in rules )
            {
                // All Users
                if ( authRule.SpecialRole == SpecialRole.AllUsers )
                {
                    matchFound = true;
                    authorized = authRule.AllowOrDeny == "A";
                    return authRule;
                }

                // All Authenticated Users
                if ( !matchFound && authRule.SpecialRole == SpecialRole.AllAuthenticatedUsers && person != null )
                {
                    matchFound = true;
                    authorized = authRule.AllowOrDeny == "A";
                    return authRule;
                }

                // All Unauthenticated Users
                if ( !matchFound && authRule.SpecialRole == SpecialRole.AllUnAuthenticatedUsers && person == null )
                {
                    matchFound = true;
                    authorized = authRule.AllowOrDeny == "A";
                    return authRule;
                }

                if ( !matchFound && authRule.SpecialRole == SpecialRole.None && person != null )
                {
                    // See if person has been authorized to entity
                    if ( authRule.PersonAliasId.HasValue &&
                        person.Aliases.Where( a => a.Id == authRule.PersonAliasId.Value ).Any() )
                    {
                        matchFound = true;
                        authorized = authRule.AllowOrDeny == "A";
                        return authRule;
                    }

                    // See if person is in role authorized
                    if ( !matchFound && authRule.GroupId.HasValue )
                    {
                        Role role = Role.Read( authRule.GroupId.Value );
                        if ( role != null && role.IsPersonInRole( person.Guid ) )
                        {
                            matchFound = true;
                            authorized = authRule.AllowOrDeny == "A";
                            return authRule;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// This is a heavily modified version of the Security.ItemAuthorized private method. It should perform
        /// nearly 99% of the same functionality. It's use is to find the explicit Auth record that matches the
        /// entity in question.
        /// </summary>
        /// <param name="entity">The entity whose auth records we are searching for.</param>
        /// <param name="action">The action that is to be authorized.</param>
        /// <param name="person">The person requesting permissions.</param>
        /// <param name="isRootEntity">True if this is the first call, false when called recursively.</param>
        /// <param name="checkParentAuthority">True if the parent authorities should be checked, otherwise false.</param>
        /// <param name="authoritative">On return this contains the ISecured object that had the explicit Auth record defined.</param>
        /// <returns>An Auth object if a permission was found, otherwise null.</returns>
        Auth ItemAuthorized( ISecured entity, string action, Person person, bool isRootEntity, bool checkParentAuthority, out ISecured authoritative )
        {
            var auth = MatchingAuth( entity, action, person );
            if ( auth != null )
            {
                authoritative = entity;
                return auth;
            }

            if ( checkParentAuthority )
            {
                Auth parentAuthorized = null;
                ISecured parentAuthority = null;

                if ( isRootEntity && entity.ParentAuthorityPre != null )
                {
                    parentAuthorized = ItemAuthorized( entity.ParentAuthorityPre, action, person, false, false, out parentAuthority );
                }

                if ( parentAuthorized == null && entity.ParentAuthority != null )
                {
                    parentAuthorized = ItemAuthorized( entity.ParentAuthority, action, person, false, true, out parentAuthority );
                }

                if ( parentAuthorized != null )
                {
                    authoritative = parentAuthority;
                    return parentAuthorized;
                }
            }

            authoritative = null;
            return null;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles the BlockUpdated event of the control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void Block_BlockUpdated( object sender, EventArgs e )
        {
        }

        /// <summary>
        /// Handles the Click event of the control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void btnCheck_Click( object sender, EventArgs e )
        {
            var entityId = tbEntityId.Text.AsInteger();
            var entityGuid = tbEntityId.Text.AsGuid();
            var entityType = new EntityTypeService( new RockContext() ).Get( pEntityType.SelectedEntityTypeId.Value );
            IEntity entity = null;
            ISecured entitySecured = null;

            pnlResult.Visible = false;
            nbWarning.Text = string.Empty;

            //
            // Find the entity they are searching for or display an error if we couldn't parse the Id.
            //
            if ( entityId != 0 )
            {
                entity = GetById( entityType.Name, entityId );
            }
            else if ( entityGuid != Guid.Empty )
            {
                entity = GetByGuid( entityType.Name, entityGuid );
            }
            else
            {
                nbWarning.Text = "Could not parse the entity id.";
                return;
            }

            //
            // If the entity was not found, display an error.
            //
            if ( entity == null )
            {
                nbWarning.Text = "Could not find the entity, maybe the wrong Id was specified.";
                return;
            }

            //
            // Make sure this entity supports security.
            //
            entitySecured = entity as ISecured;
            if ( entitySecured == null )
            {
                nbWarning.Text = "Entity type is not a securable item.";
                return;
            }

            //
            // Get the selected person or use the currently logged in person.
            //
            var person = ppPerson.PersonId.HasValue ? new PersonService( new RockContext() ).Get( ppPerson.PersonId.Value ) : CurrentPerson;

            //
            // Walk all the supported actions and build a row of results.
            //
            List<AuthGridRow> rows = new List<AuthGridRow>();
            foreach ( var action in entitySecured.SupportedActions )
            {
                ISecured authorative = null;
                var auth = ItemAuthorized( entitySecured, action.Key, person, true, true, out authorative );

                if ( auth != null )
                {
                    var authEntity = authorative as IEntity;
                    string friendlyName = "Unknown";

                    if ( auth.SpecialRole != SpecialRole.None )
                        friendlyName = auth.SpecialRole.ToStringSafe().SplitCase();
                    else if ( auth.PersonAlias != null )
                        friendlyName = auth.PersonAlias.ToStringSafe();
                    else if ( auth.Group != null )
                        friendlyName = auth.Group.ToStringSafe();

                    var row = new AuthGridRow();
                    row.Action = auth.Action;
                    if ( authEntity != null )
                    {
                        row.EntityType = authEntity.TypeName;
                        row.EntityId = authEntity.Id != 0 ? ( int? ) authEntity.Id : null;
                        row.EntityName = authEntity.Id != 0 ? authEntity.ToString() : "(Entity Administration Security)";
                    }
                    else
                    {
                        row.EntityId = null;

                        if ( authorative as GlobalDefault != null )
                        {
                            row.EntityType = "(Global Default)";
                            row.EntityName = string.Empty;
                        }
                        else
                        {
                            row.EntityType = "Unknown";
                            row.EntityName = "Unknown";
                        }
                    }
                    row.Access = auth.AllowOrDeny == "A" ? "<span class='label label-success'>Allow</span>" : "<span class='label label-danger'>Deny</span>";
                    row.Role = friendlyName;

                    rows.Add( row );
                }
                else
                {
                    var row = new AuthGridRow();
                    row.Action = action.Key;
                    row.EntityType = string.Empty;
                    row.EntityId = null;
                    row.EntityName = string.Empty;
                    row.Access = "<span class='label label-default'>Unknown</span>";
                    row.Role = "No explicit permissions found";

                    rows.Add( row );
                }
            }

            gResults.DataSource = rows;
            gResults.DataBind();

            pnlResult.Visible = true;
        }

        #endregion

        /// <summary>
        /// Helper class for displaying results in a grid.
        /// </summary>
        class AuthGridRow
        {
            public string Action { get; set; }

            public string EntityType { get; set; }

            public string EntityName { get; set; }

            public int? EntityId { get; set; }

            public string Access { get; set; }

            public string Role { get; set; }
        }
    }
}