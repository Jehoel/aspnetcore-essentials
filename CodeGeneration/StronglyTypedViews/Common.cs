using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using Microsoft.Extensions.DependencyInjection;

namespace YourProject
{
	public static class StronglyTypedViewExtensions
	{
		public static StronglyTypedViews Views( this Controller controller ) => new StronglyTypedViews( controller );

		public static StronglyTypedPartialViews PartialViews( this IHtmlHelper htmlHelper ) => new StronglyTypedPartialViews( htmlHelper );

		public static StronglyTypedPartialViews<TModel> PartialViews<TModel>( this IHtmlHelper<TModel> htmlHelper ) => new StronglyTypedPartialViews<TModel>( htmlHelper );

		public static TData RequireViewData<TData>( this ViewDataDictionary vdd )
		{
			String key = typeof(TData).Name;
			if( vdd.TryGetValue( key, out Object? obj ) && obj is TData data )
			{
				return data;
			}
			else
			{
				throw new InvalidOperationException( "Expected ViewDataDictionary[\"" + key + "\"] but encountered " + ( obj?.ToString() ?? "null" ) + " instead." );
			}
		}

		public static void SetViewData<TData>( this ViewDataDictionary vdd, TData data )
		{
			String key = typeof(TData).Name;
			if( vdd.TryGetValue( key, out Object? obj ) && obj != null )
			{
				if( Object.Equals( data, obj ) )
				{
					// OK. NOOP.
				}
				else
				{
					throw new InvalidOperationException( "ViewDataDictionary[\"" + key + "\"] is already set to a different object reference." );
				}
			}
			else
			{
				vdd.Add( key, data );
			}
		}
	}

	public static partial class ViewNames
	{
	}

	public static partial class PartialViewNames
	{
	}

	public static partial class ContentFiles
	{
	}

	public readonly partial struct StronglyTypedViews
	{
		private readonly Controller controller;

		public StronglyTypedViews( Controller controller )
		{
			this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
		}
	}

	public readonly partial struct StronglyTypedPartialViews
	{
		private readonly IHtmlHelper htmlHelper;

		public StronglyTypedPartialViews( IHtmlHelper htmlHelper )
		{
			this.htmlHelper = htmlHelper ?? throw new ArgumentNullException( nameof(htmlHelper) );
		}
	}

	public readonly partial struct StronglyTypedPartialViews<TModel>
	{
		public static implicit operator StronglyTypedPartialViews( StronglyTypedPartialViews<TModel> self )
		{
			return new StronglyTypedPartialViews( htmlHelper: self.htmlHelper );
		}

		private readonly IHtmlHelper<TModel> htmlHelper;
//		private readonly IModelExpressionProvider modelExpressionProvider; // While ASP.NET Core registers both `IModelExpressionProvider` and `ModelExpressionProvider` (as `IModelExpressionProvider`) as singleton, it's probably better to just get the concrete type directly.
		private readonly ModelExpressionProvider modelExpressionProvider;

		public StronglyTypedPartialViews( IHtmlHelper<TModel> htmlHelper )
		{
			this.htmlHelper = htmlHelper ?? throw new ArgumentNullException( nameof(htmlHelper) );
			this.modelExpressionProvider = htmlHelper.ViewContext.HttpContext.RequestServices.GetRequiredService<ModelExpressionProvider>();
		}

#region RenderPartialAsync

		internal ViewDataDictionary<TPartialViewModel> CreateViewDataDictionaryForViewModel<TPartialViewModel>( TPartialViewModel model )
		{
			return new ViewDataDictionary<TPartialViewModel>( metadataProvider: this.htmlHelper.MetadataProvider, modelState: this.htmlHelper.ViewContext.ModelState ) { Model = model };
		}

		internal ViewDataDictionary<TPartialViewModel> CreateViewDataDictionaryForModelExpression<TPartialViewModel>( Expression<Func<TModel,TPartialViewModel>> forExpr )
		{
			ModelExpression modelExpression = this.modelExpressionProvider.CreateModelExpression( this.htmlHelper.ViewData, expression: forExpr );

			if( modelExpression.Model is TPartialViewModel partialViewModel )
			{
				ViewDataDictionary<TPartialViewModel> childViewData = new ViewDataDictionary<TPartialViewModel>( source: this.htmlHelper.ViewData, model: partialViewModel );

				childViewData.TemplateInfo.HtmlFieldPrefix = childViewData.TemplateInfo.GetFullHtmlFieldName( modelExpression.Name );

				return childViewData;
			}
			else
			{
				const String MSG_FMT = "Expected " + nameof(forExpr) + " to evaluate to a non-null reference to an instance of {0} but actually encountered {1}.";
				throw new InvalidOperationException( MSG_FMT.FmtInv( typeof(TPartialViewModel).FullName!, modelExpression.Model?.GetType().FullName ?? "a null reference" ) );
			}
		}

		private async Task RenderPartialOuterAsync<TPartialModel>( String name, ViewDataDictionary<TPartialModel> childVdd )
		{
			await this.htmlHelper.RenderPartialAsync( partialViewName: name, model: childVdd.Model, viewData: childVdd ).ConfigureAwait(false);
		}

		private async Task RenderPartialOuterAsync<TPartialModel,TData0>( String name, ViewDataDictionary<TPartialModel> childVdd, TData0 data0 )
		{
			childVdd.SetViewData<TData0>( data0 );
			await this.RenderPartialOuterAsync( name, childVdd );
		}

		private async Task RenderPartialOuterAsync<TPartialModel,TData0,TData1>( String name, ViewDataDictionary<TPartialModel> childVdd, TData0 data0, TData1 data1 )
		{
			childVdd.SetViewData<TData1>( data1 );
			await this.RenderPartialOuterAsync<TPartialModel,TData0>( name, childVdd, data0 );
		}

		private async Task RenderPartialOuterAsync<TPartialModel,TData0,TData1,TData2>( String name, ViewDataDictionary<TPartialModel> childVdd, TData0 data0, TData1 data1, TData2 data2 )
		{
			childVdd.SetViewData<TData2>( data2 );
			await this.RenderPartialOuterAsync<TPartialModel,TData0,TData1>( name, childVdd, data0, data1 );
		}

#endregion
	}
}