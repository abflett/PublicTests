using MCSS.Application.Services.Products.Commands;
using MCSS.Application.Services.References.Commands;
using MCSS.Application.Services.References.Queries;
using MCSS.Core.Common.Exceptions;
using MCSS.Core.Common.Helpers;
using MCSS.Core.Models;
using MCSS.Infrastructure.Data;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MCSS.Application.Services.Products.CommandHandler
{


    public class ProductUpdateHandler : IRequestHandler<ProductUpdateCommand, Product>
    {
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IMediator _mediator;

        public ProductUpdateHandler(ApplicationDbContext applicationDbContext, IWebHostEnvironment webHostEnvironment, IMediator mediator)
        {
            _applicationDbContext = applicationDbContext;
            _webHostEnvironment = webHostEnvironment;
            _mediator = mediator;
        }

        public async Task<Product> Handle(ProductUpdateCommand request, CancellationToken cancellationToken)
        {
            var existing = await _applicationDbContext.Products
                .Include(x => x.Categories)
                .Include(x => x.Models)
                .Include(x => x.ProductUi!.ProductUiFiles)
                .Include(x => x.References)
                .Include(x => x.ReferenceProducts).AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProductId == request.Product.ProductId, cancellationToken: cancellationToken);

            if (existing == null)
                throw new NotFoundException($"Product/{request.Product.ProductId}");

            var cleanedProduct = request.Product;
            cleanedProduct.Status = null;
            cleanedProduct.Brand = null;
            cleanedProduct.Department = null;
            cleanedProduct.CartItems = null;
            cleanedProduct.OrderDetails = null;

            // fix stock dateTime values
            if (cleanedProduct.Stock != null && cleanedProduct.Stock!.Count > 0)
            {
                foreach (var stockItem in cleanedProduct.Stock!)
                {
                    stockItem.LastOrder = DateTools.SetKindUtc(stockItem.LastOrder);
                    stockItem.OrderedLast = DateTools.SetKindUtc(stockItem.OrderedLast);
                }
            }

            // remove old values for referenceProducts
            // get old reference
            // Todo: Remove old values, buggy
            List<ReferenceProduct> referenceProducts = new();
            List<ReferenceProduct> deleteReferenceProducts = new();
            foreach (var reference in cleanedProduct.References!)
            {
                if (reference.ReferenceId > 0)
                {
                    var oldReference = await _mediator.Send(new ReferenceByIdQuery(reference.ReferenceId), cancellationToken);

                    foreach (var oldReferenceProduct in oldReference.ReferenceProducts!)
                    {
                        if (!reference.ReferenceProducts!.Any(x => x.ProductId == oldReferenceProduct.ProductId))
                        {
                            deleteReferenceProducts.Add(oldReferenceProduct);
                        }
                    }
                }
            }

            await _mediator.Send(new ReferenceProductDeleteCommand(deleteReferenceProducts));

            List<int> existingCategoryIds = new();
            foreach (var category in existing.Categories!)
            {
                existingCategoryIds.Add(category.CategoryId);
            }

            var categoriesList = new List<Category>();
            foreach (Category category in cleanedProduct.Categories!)
            {
                if (!existingCategoryIds.Any(x => x == category.CategoryId))
                    categoriesList.Add(category);
            }
            cleanedProduct.Categories = categoriesList;

            // fix product discount date kind to utc kind
            if (cleanedProduct.ProductDiscounts != null && cleanedProduct.ProductDiscounts!.Count > 0)
            {
                foreach (var productDiscount in cleanedProduct.ProductDiscounts!)
                {
                    productDiscount.ValidFrom = DateTools.SetKindUtc(productDiscount.ValidFrom);
                    productDiscount.Expires = DateTools.SetKindUtc(productDiscount.Expires);
                }
            }

            if (cleanedProduct.Coupons != null && cleanedProduct.Coupons!.Count > 0)
            {
                foreach (var coupon in cleanedProduct.Coupons!)
                {
                    coupon.ValidFrom = DateTools.SetKindUtc(coupon.ValidFrom);
                    coupon.Expires = DateTools.SetKindUtc(coupon.Expires);
                }
            }

            List<Model> modelList = new();
            if (cleanedProduct.Models != null && cleanedProduct.Models.Count > 0)
            {
                foreach (var model in cleanedProduct.Models)
                {
                    if (model.ModelId == 0)
                        modelList.Add(model);
                }
            }
            cleanedProduct.Models!.Clear();
            cleanedProduct.Models = modelList;

            List<Option> optionList = new();
            if (cleanedProduct.Options != null && cleanedProduct.Options.Count > 0)
            {
                foreach (var option in cleanedProduct.Options)
                {
                    if (option.OptionId == 0)
                        optionList.Add(option);
                }
            }
            cleanedProduct.Options!.Clear();
            cleanedProduct.Options = optionList;

            EntityEntry<Product> updateResult;
            int saveResult = 0;
            try
            {
                updateResult = _applicationDbContext.Products.Update(cleanedProduct);
                saveResult = await _applicationDbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                throw;
            }

            if (saveResult <= 0)
                throw new BadRequestException();

            foreach (var productUifile in cleanedProduct.ProductUi!.ProductUiFiles!)
            {
                if (productUifile.FormContent != null)
                {
                    var directoryPath = Path.Combine(_webHostEnvironment.WebRootPath, productUifile.Folder);
                    var filePath = Path.Combine(directoryPath, productUifile.Filename);
                    if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
                    File.WriteAllBytes(filePath, productUifile.FormContent);

                    if (existing.ProductUi != null)
                    {
                        ProductUiFile? oldProductUi = existing.ProductUi!.ProductUiFiles!.FirstOrDefault(x => x.ProductUiFileId == productUifile.ProductUiFileId);
                        if (oldProductUi != null)
                        {
                            var foundFile = Path.Combine(directoryPath, oldProductUi.Filename);
                            if (File.Exists(foundFile))
                                File.Delete(foundFile);
                        }
                    }
                }
            }

            return updateResult.Entity;
        }
    }
}
