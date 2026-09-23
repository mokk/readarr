using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddBookFixture : CoreTest<AddBookService>
    {
        private Author _fakeAuthor;
        private Book _fakeBook;

        [SetUp]
        public void Setup()
        {
            _fakeAuthor = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .With(s => s.Metadata = Builder<AuthorMetadata>.CreateNew().Build())
                .Build();
        }

        private void GivenValidBook(string bookId, string editionId)
        {
            _fakeBook = Builder<Book>
                .CreateNew()
                .With(x => x.Editions = Builder<Edition>
                      .CreateListOfSize(1)
                      .TheFirst(1)
                      .With(e => e.ForeignEditionId = editionId)
                      .With(e => e.Monitored = true)
                      .BuildList())
                .Build();

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo(bookId))
                .Returns(Tuple.Create(_fakeAuthor.Metadata.Value.ForeignAuthorId,
                                      _fakeBook,
                                      new List<AuthorMetadata> { _fakeAuthor.Metadata.Value }));

            Mocker.GetMock<IAddAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns(_fakeAuthor);
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null))
                  .Returns<Author, NamingConfig>((c, n) => c.Name);
        }

        private Book BookToAdd(string editionId, string bookId, string authorId)
        {
            return new Book
            {
                ForeignBookId = bookId,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = editionId,
                        Monitored = true
                    }
                },
                AuthorMetadata = new AuthorMetadata
                {
                    ForeignAuthorId = authorId
                }
            };
        }

        [Test]
        public void should_be_able_to_add_a_book_without_passing_in_name()
        {
            var newBook = BookToAdd("edition", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            var book = Subject.AddBook(newBook);

            book.Title.Should().Be(_fakeBook.Title);
        }

        [Test]
        public void should_keep_db_ids_of_existing_editions_when_adding_edition_to_existing_book()
        {
            var newBook = BookToAdd("12345", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            var dbBook = Builder<Book>.CreateNew().With(x => x.Id = 7).With(x => x.ForeignBookId = "book").Build();
            var dbEdition = Builder<Edition>.CreateNew().With(x => x.Id = 42).With(x => x.BookId = 7).With(x => x.ForeignEditionId = "edition").With(x => x.Monitored = true).Build();

            // metadata lookup replaces the foreign id with the fake book's, so match any id
            Mocker.GetMock<IBookService>()
                  .Setup(s => s.FindById(It.IsAny<string>()))
                  .Returns(dbBook);

            Mocker.GetMock<IEditionService>()
                  .Setup(s => s.GetEditionsByBook(7))
                  .Returns(new List<Edition> { dbEdition });

            var translated = Builder<Book>
                .CreateNew()
                .With(x => x.Editions = new List<Edition>
                {
                    Builder<Edition>.CreateNew().With(e => e.Id = 0).With(e => e.ForeignEditionId = "12345").With(e => e.Monitored = true).Build()
                })
                .Build();

            Mocker.GetMock<ISearchForNewBook>()
                  .Setup(s => s.SearchByGoodreadsBookId(12345, false))
                  .Returns(new List<Book> { translated });

            var book = Subject.AddBook(newBook);

            book.Id.Should().Be(7);
            book.Editions.Value.Should().HaveCount(2);
            book.Editions.Value.Single(x => x.ForeignEditionId == "edition").Id.Should().Be(42);
            book.Editions.Value.Single(x => x.ForeignEditionId == "edition").Monitored.Should().BeFalse();
            book.Editions.Value.Single(x => x.ForeignEditionId == "12345").Id.Should().Be(0);
            book.Editions.Value.Single(x => x.ForeignEditionId == "12345").Monitored.Should().BeTrue();
        }

        [Test]
        public void should_throw_if_missing_edition_id_is_not_numeric()
        {
            // the metadata server only returns a subset of a work's editions,
            // so the edition the user picked may not be in the work
            var newBook = BookToAdd("not-a-goodreads-id", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            Assert.Throws<ValidationException>(() => Subject.AddBook(newBook));

            Mocker.GetMock<ISearchForNewBook>()
                  .Verify(s => s.SearchByGoodreadsBookId(It.IsAny<int>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_add_directly_fetched_edition_and_monitor_it()
        {
            var newBook = BookToAdd("12345", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            var translated = Builder<Book>
                .CreateNew()
                .With(x => x.Editions = new List<Edition>
                {
                    Builder<Edition>
                        .CreateNew()
                        .With(e => e.ForeignEditionId = "12345")
                        .With(e => e.Monitored = true)
                        .Build()
                })
                .Build();

            Mocker.GetMock<ISearchForNewBook>()
                  .Setup(s => s.SearchByGoodreadsBookId(12345, false))
                  .Returns(new List<Book> { translated });

            var book = Subject.AddBook(newBook);

            book.Editions.Value.Should().HaveCount(2);
            book.Editions.Value.Should().ContainSingle(x => x.Monitored);
            book.Editions.Value.Single(x => x.Monitored).ForeignEditionId.Should().Be("12345");
            book.Editions.Value.Single(x => x.Monitored).ManualAdd.Should().BeTrue();
        }

        [Test]
        public void should_throw_if_edition_cannot_be_found_anywhere()
        {
            var newBook = BookToAdd("12345", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            Mocker.GetMock<ISearchForNewBook>()
                  .Setup(s => s.SearchByGoodreadsBookId(12345, false))
                  .Returns(new List<Book>());

            Assert.Throws<ValidationException>(() => Subject.AddBook(newBook));

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_throw_if_book_cannot_be_found()
        {
            var newBook = BookToAdd("edition", "book", "author");

            Mocker.GetMock<IProvideBookInfo>()
                  .Setup(s => s.GetBookInfo("book"))
                  .Throws(new BookNotFoundException("edition"));

            Assert.Throws<ValidationException>(() => Subject.AddBook(newBook));

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
